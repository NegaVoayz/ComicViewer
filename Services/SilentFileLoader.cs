using ComicViewer.Database;
using ComicViewer.Models;
using SharpCompress.Archives;
using SharpCompress.Archives.Tar;
using SharpCompress.Common;
using SharpCompress.Common.GZip;
using SharpCompress.Common.Zip;
using SharpCompress.Writers;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;
using System.IO.MemoryMappedFiles;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using System.Windows.Shapes;

namespace ComicViewer.Services
{
    public class SilentFileLoader
    {
        private readonly ConcurrentDictionary<string, (Task task, CancellationTokenSource cts)> _runningTasks
        = new ConcurrentDictionary<string, (Task, CancellationTokenSource)>();

        private readonly object _syncLock = new object();

        private static readonly Lazy<SilentFileLoader> _instance = new(() => new SilentFileLoader());
        public static SilentFileLoader Instance => _instance.Value;
        public async Task<bool> StopMovingTask(string Key)
        {
            if (!_runningTasks.TryGetValue(Key, out var taskInfo))
            {
                return false;
            }
            taskInfo.cts.Cancel();
            await taskInfo.task.ConfigureAwait(false);
            return true;
        }
        public async Task RecoverMovingTask()
        {
            await Task.Run(async () =>
            {
                var movingTasks = await ComicService.Instance.GetAllMovingFilesAsync();
                foreach (var movingFile in movingTasks)
                {
                    StartMovingTask(movingFile);
                }
            });
        }
        public void AddMovingTask(MovingFileModel model)
        {
            var same_target_task = ComicService.Instance.GetMovingTask(model.Key);
            if (same_target_task == null)
            {
                ComicService.Instance.AddMovingTask(model);
                StartMovingTask(model);
                return;
            }
            if (same_target_task.DestinationPath == model.DestinationPath)
            {
                return; // of course
            }
            if (same_target_task.DestinationPath != model.SourcePath)
            {
                return; // impossible
            }
            if (same_target_task.SourcePath == model.DestinationPath)
            {
                if (_runningTasks.TryGetValue(model.Key, out var taskInfo))
                {
                    taskInfo.cts.Cancel();
                }
                else
                {
                    _ = CleanupFileAsync(model.DestinationPath);
                }
                return; // no move
            }
            _ = CancelAndStartNewTaskAsync(model.Key,
                new MovingFileModel
                {
                    Key = model.Key,
                    DestinationPath = model.DestinationPath,
                    SourcePath = same_target_task.SourcePath
                });
        }
        private async Task CancelAndStartNewTaskAsync(string key, MovingFileModel model)
        {
            if (_runningTasks.TryGetValue(key, out var taskInfo))
            {
                taskInfo.cts.Cancel();
                await taskInfo.task.ConfigureAwait(false);
            }
            ComicService.Instance.AddMovingTask(model);
            StartMovingTask(model);
        }
        private void StartMovingTask(MovingFileModel model)
        {
            var cts = new CancellationTokenSource();

            var task = Task.Run(async () =>
            {
                try
                {
                    await MoveComicAsync(model, cts.Token);
                    // 任务成功完成
                }
                catch (OperationCanceledException)
                { /*任务被取消*/ }
                catch (Exception ex)
                { /* 任务失败 */ }
                finally
                {
                    // 清理：从运行任务字典中移除
                    _runningTasks.TryRemove(model.Key, out _);
                    cts.Dispose();
                }
            }, cts.Token);

            // 存储任务和取消令牌
            _runningTasks[model.Key] = (task, cts);
        }

        private async Task MoveComicAsync(MovingFileModel model, CancellationToken cancellation = default)
        {
            if (!File.Exists(model.SourcePath))
            {
                throw new FileNotFoundException("源文件不存在", model.SourcePath);
            }
            string srcExt = System.IO.Path.GetExtension(model.SourcePath);
            string dstExt = System.IO.Path.GetExtension(model.DestinationPath);

            try
            {
                cancellation.ThrowIfCancellationRequested();
                ComicFileService.Instance.AddComicPath(model.Key, model.SourcePath);
                if (srcExt == ".cmc")
                {
                    await LoadCMCAsync(model, cancellation);
                }
                else if (srcExt == ".zip")
                {
                    await Task.Run(() => File.Copy(model.SourcePath, model.DestinationPath));
                }
                else
                {
                    await LoadCompressedAsync(model, cancellation);
                }
                cancellation.ThrowIfCancellationRequested();
                ComicFileService.Instance.RemoveComicPath(model.Key);
                await ComicService.Instance.DoneMovingTaskAsync(model);
                return;
            }
            catch(OperationCanceledException)
            {
                ComicFileService.Instance.ReleaseComicPath(model.Key);
                await CleanupFileAsync(model.DestinationPath);
            }
            catch
            {
                // 其他异常也清理
                ComicFileService.Instance.RemoveComicPath(model.Key);
                await CleanupFileAsync(model.DestinationPath);
                // here we don't remove an error task from record
                //Todo: maybe add retry
                throw;
            }
            finally
            {
            }
        }
        private async Task LoadCompressedAsync(MovingFileModel model, CancellationToken cancellation)
        {
            await Task.Run(async () =>
            {
                using var sourceArchive = ArchiveFactory.Open(model.SourcePath);
                var entries = sourceArchive.Entries
                    .Where(e => !e.IsDirectory)
                    .ToArray();

                using var outputStream = new FileStream(model.DestinationPath, FileMode.Create,
                                                       FileAccess.Write, FileShare.None,
                                                       131072);
                using var zipWriter = WriterFactory.Open(outputStream, ArchiveType.Zip,
                                                        new WriterOptions(CompressionType.Deflate));

                // 创建Channel作为缓冲区队列
                var channel = Channel.CreateBounded<(string Key, MemoryStream Stream)>(
                    new BoundedChannelOptions(2)  // 双缓冲容量
                    {
                        FullMode = BoundedChannelFullMode.Wait  // 队列满时等待
                    });

                // 生产者：读取文件
                var producer = Task.Run(async () =>
                {
                    foreach (var entry in entries)
                    {
                        cancellation.ThrowIfCancellationRequested();

                        using var entryStream = entry.OpenEntryStream();
                        var memoryStream = new MemoryStream();
                        await entryStream.CopyToAsync(memoryStream, cancellation);
                        memoryStream.Position = 0;

                        await channel.Writer.WriteAsync((entry.Key, memoryStream), cancellation);
                    }

                    channel.Writer.Complete();
                }, cancellation);

                // 消费者：写入ZIP
                var consumer = Task.Run(async () =>
                {
                    await foreach (var item in channel.Reader.ReadAllAsync(cancellation))
                    {
                        zipWriter.Write(item.Key, item.Stream);
                        item.Stream.Dispose();
                    }
                }, cancellation);

                // 等待生产和消费完成
                await Task.WhenAll(producer, consumer);

            }, cancellation);
        }
        private async Task LoadCMCAsync(MovingFileModel model, CancellationToken cancellation)
        {
            using var archive = TarArchive.Open(model.SourcePath);

            var tarEntry = archive.Entries.FirstOrDefault(e =>
                e.Key.Equals("comic.zip", StringComparison.OrdinalIgnoreCase));

            if (tarEntry == null || tarEntry.IsDirectory) return;

            string destPath = model.DestinationPath;
            long fileSize = tarEntry.Size;

            // 预分配空间
            using (var fileStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fileStream.SetLength(fileSize);
            }

            using var mmf = MemoryMappedFile.CreateFromFile(destPath, FileMode.Open, null, fileSize);
            using var entryStream = tarEntry.OpenEntryStream();

            const int segmentSize = 4 * 1024 * 1024;
            int segmentCount = (int)((fileSize + segmentSize - 1) / segmentSize);

            var writeTasks = new List<Task>();

            // 串行读取，并行写入
            for (int i = 0; i < segmentCount; i++)
            {
                cancellation.ThrowIfCancellationRequested();

                long offset = i * segmentSize;
                long size = Math.Min(segmentSize, fileSize - offset);

                // 读取当前分段
                byte[] buffer = new byte[size];
                int totalRead = 0;

                while (totalRead < size)
                {
                    int bytesRead = await entryStream.ReadAsync(
                        buffer.AsMemory(totalRead, (int)(size - totalRead)),
                        cancellation);

                    if (bytesRead == 0) break;
                    totalRead += bytesRead;
                }

                // 如果读取的数据量不对，抛出异常
                if (totalRead != size)
                    throw new IOException($"Failed to read complete segment. Expected: {size}, Read: {totalRead}");

                // 启动异步写入任务
                var writeTask = Task.Run(() =>
                {
                    using var segmentStream = mmf.CreateViewStream(offset, size, MemoryMappedFileAccess.Write);
                    segmentStream.Write(buffer, 0, buffer.Length);
                }, cancellation);

                writeTasks.Add(writeTask);
            }

            // 等待所有写入完成
            await Task.WhenAll(writeTasks);

            cancellation.ThrowIfCancellationRequested();
        }

        private async Task CleanupFileAsync(string filePath)
        {
            try
            {
                await Task.Run(() =>
                {
                    if (File.Exists(filePath))
                    {
                        // 尝试多次删除
                        for (int i = 0; i < 3; i++)
                        {
                            try
                            {
                                File.Delete(filePath);
                                return;
                            }
                            catch (IOException) when (i < 2)
                            {
                                // 等待后重试
                                Thread.Sleep(100 * (i + 1));
                            }
                        }
                    }
                });
            }
            catch
            {
                // 忽略清理失败
            }
        }
    }

}
