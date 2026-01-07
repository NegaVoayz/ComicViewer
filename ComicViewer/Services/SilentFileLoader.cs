using ComicViewer.Infrastructure;
using ComicViewer.Models;
using SharpCompress.Archives;
using SharpCompress.Archives.Tar;
using SharpCompress.Common;
using SharpCompress.Writers;
using System.Collections.Concurrent;
using System.IO;
using System.IO.MemoryMappedFiles;

namespace ComicViewer.Services
{
    public class SilentFileLoader
    {
        private readonly ConcurrentDictionary<string, (Task task, CancellationTokenSource cts)> _runningTasks
        = new ConcurrentDictionary<string, (Task, CancellationTokenSource)>();

        private readonly ComicService service;

        public SilentFileLoader(ComicService service)
        {
            this.service = service;
            service.Load.Add(new DAGTask
            {
                name = "FileLoader",
                task = RecoverLoads,
                requirements = { "DataService", "FileService" }
            });
        }

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

        private async Task RecoverLoads()
        {
            var movingTasks = await service.DataService.GetAllMovingFilesAsync();
            foreach (var movingTask in movingTasks)
            {
                // if the source is gone, remove it.
                if (!File.Exists(movingTask.SourcePath))
                {
                    await service.DataService.DoneMovingTaskAsync(movingTask);
                    await service.DataService.RemoveComicAsync(movingTask.Key);
                    continue;
                }
                service.FileService.AddComicTempPath(movingTask.Key, movingTask.SourcePath);
                StartMovingTask(movingTask, true);
            }
        }
        public async Task AddMovingTask(string Key, string sourcePath)
        {
            var model = new MovingFileModel
            {
                Key = Key,
                SourcePath = sourcePath,
                DestinationPath = System.IO.Path.Combine(Configs.GetFilePath(), $"{Key}.zip")
            };
            await AddMovingTask(model);
        }
        public async Task AddMovingTask(MovingFileModel model)
        {
            var same_target_task = service.DataService.GetMovingTask(model.Key);
            if (same_target_task == null)
            {
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
            await CancelAndStartNewTaskAsync(
                new MovingFileModel
                {
                    Key = model.Key,
                    DestinationPath = model.DestinationPath,
                    SourcePath = same_target_task.SourcePath
                });
        }
        private async Task CancelAndStartNewTaskAsync(MovingFileModel model)
        {
            if (_runningTasks.TryGetValue(model.Key, out var taskInfo))
            {
                taskInfo.cts.Cancel();
                await taskInfo.task.ConfigureAwait(false);
            }
            StartMovingTask(model);
        }
        private void StartMovingTask(MovingFileModel model, bool is_recover = false)
        {
            var cts = new CancellationTokenSource();

            var task = Task.Run(async () =>
            {
                try
                {
                    if (!is_recover)
                    {
                        await service.DataService.AddMovingTask(model);
                    }
                    //Thread.Sleep(120 * 1000);
                    await MoveComicAsync(model, cts.Token);
                    // 任务成功完成
                }
                catch (OperationCanceledException)
                { /*任务被取消*/ }
                catch (Exception)
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
            try
            {
                if (!File.Exists(model.SourcePath))
                {
                    throw new FileNotFoundException("源文件不存在", model.SourcePath);
                }
                string srcExt = System.IO.Path.GetExtension(model.SourcePath);
                string dstExt = System.IO.Path.GetExtension(model.DestinationPath);

                cancellation.ThrowIfCancellationRequested();
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
                service.FileService.GenerateComicPath(model.Key);
            }
            catch (OperationCanceledException)
            {
                await CleanupFileAsync(model.DestinationPath);
            }
            catch
            {
                // 其他异常也清理
                await CleanupFileAsync(model.DestinationPath);
                await service.DataService.DoneMovingTaskAsync(model);
                await service.DataService.RemoveComicAsync(model.Key);
                throw;
            }
            finally
            {
                service.FileService.RemoveComicTempPath(model.Key);
                await service.DataService.DoneMovingTaskAsync(model);
            }
            return;
        }
        private async Task LoadCompressedAsync(
            MovingFileModel model,
            CancellationToken cancellation)
        {
            using var archive = ArchiveFactory.Open(model.SourcePath);
            using var reader = archive.ExtractAllEntries();

            using var output = new FileStream(
                model.DestinationPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1024 * 1024);

            using var zipWriter = WriterFactory.Open(
                output,
                ArchiveType.Zip,
                new WriterOptions(CompressionType.Deflate));

            // 顺序解压

            while (reader.MoveToNextEntry())
            {
                cancellation.ThrowIfCancellationRequested();

                if (reader.Entry.Size == 0)
                    continue;
                if (reader.Entry.Key == null)
                    continue;

                using var entryStream = reader.OpenEntryStream();
                zipWriter.Write(reader.Entry.Key, entryStream);
            }
        }


        private async Task LoadCMCAsync(MovingFileModel model, CancellationToken cancellation)
        {
            using var archive = TarArchive.Open(model.SourcePath);

            var tarEntry = archive.Entries.FirstOrDefault(e => e.Key != null &&
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
