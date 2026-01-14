using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ComicViewer.Services
{
    public class ComicUtils
    {
        public static readonly char[] AuthorDelimiterChars = [',', ';', '\t', '/'];
        public static readonly char[] TagDelimiterChars = [',', ';', '\t', ' '];
        public static readonly char[] TagAliasChars = ['/'];
        public const string AuthorPrefix = "@Author:";
        public static void AddCommentToZip(string filePath, string comment)
        {
            using (var zipStream = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite))
            {
                // Open the existing ZIP file in Update mode
                using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Update))
                {
                    // Set the archive-level comment
                    archive.Comment = comment;

                    // The comment is written when the ZipArchive object is disposed (on '}')
                }
            }
        }
        public static string GetCommentOfZip(string filePath)
        {
            using (var zipStream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            {
                try
                {
                    // Open the existing ZIP file in Update mode
                    using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Read))
                    {
                        // Get the archive-level comment
                        return archive.Comment;
                    }
                }
                catch (InvalidDataException)
                {
                    // invalid format: not ZIP
                    return String.Empty;
                }
                catch (IOException)
                {
                    // invalid format: occupied
                    return String.Empty;
                }
            }
        }
        public static string GetCombinedName(IEnumerable<string> authors, string name, IEnumerable<string> tags)
        {
            if (!tags.Any() && !authors.Any())
            {
                return name.Trim();
            }

            // 构建标签字符串，每个标签用ASCII中括号包裹
            var tagStrings = tags.Select(tag => $"[{tag}]");
            var authorStrings = authors.Select(author => $"[{author}]");

            // 用空格连接所有部分
            string result = $"{string.Join(" ", authorStrings)} {name} {string.Join(" ", tagStrings)}";

            return result.Trim();
        }
        private static (List<string> extracted, string remaining) ProcessBlocks(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return (new List<string>(), "");
            }

            List<string> extracted = new List<string>();
            StringBuilder remaining = new StringBuilder();
            StringBuilder currentBracketContent = new StringBuilder();

            bool author_part = true;

            // 定义所有可能的括号类型
            char[] openingBrackets = { '[', '【', '〔', '［', '(', '（' };  // 左括号：半角、全角、中文、其他全角
            char[] closingBrackets = { ']', '】', '〕', '］', ')', '）' };  // 右括号：半角、全角、中文、其他全角

            Stack<char> expectedClosingBracket = new();

            for (int i = 0; i < input.Length; i++)
            {
                char currentChar = input[i];

                // 检查是否是左括号
                int openingIndex = Array.IndexOf(openingBrackets, currentChar);
                if (openingIndex != -1)
                {
                    // 检查嵌套的左括号
                    if (expectedClosingBracket.Any())
                    {
                        currentBracketContent.Append(currentChar);
                    }
                    else if (author_part)
                    {
                        currentBracketContent.Append(ComicUtils.AuthorPrefix);
                    }
                    expectedClosingBracket.Push(closingBrackets[openingIndex]);
                    continue;

                }

                if (author_part && !expectedClosingBracket.Any() && !char.IsWhiteSpace(currentChar))
                {
                    author_part = false;
                }

                // 检查是否是右括号
                if (expectedClosingBracket.Any() && currentChar == expectedClosingBracket.Peek())
                {
                    expectedClosingBracket.Pop();
                    if (expectedClosingBracket.Any())
                    {
                        currentBracketContent.Append(currentChar);
                        continue;
                    }
                    // 找到完整的最外层括号对
                    string content = currentBracketContent.ToString().Trim();
                    if (!string.IsNullOrEmpty(content))
                    {
                        extracted.Add(content);
                    }

                    currentBracketContent.Clear();
                    continue;
                }

                // 处理字符
                if (expectedClosingBracket.Any())
                {
                    currentBracketContent.Append(currentChar);
                }
                else
                {
                    remaining.Append(currentChar);
                }
            }

            // 如果最后还在括号内，将内容添加到剩余字符串中
            if (expectedClosingBracket.Any())
            {
                remaining.Append('['); // 添加未匹配的左括号
                remaining.Append(currentBracketContent.ToString());
            }

            // 清理剩余字符串（去除首尾空格）
            string finalRemaining = remaining.ToString().Trim();

            return (extracted, finalRemaining);
        }
        public static ComicMetadata CreateComicDataFromFilePath(string filePath)
        {
            string fileName = Path.GetFileNameWithoutExtension(filePath);
            var result = ProcessBlocks(fileName);

            // 使用文件名作为Title
            string title = result.remaining;
            List<string> tags = result.extracted;

            // 计算MD5作为Key
            string key = CalculateMD5(title);

            // 获取文件信息
            FileInfo fileInfo = new FileInfo(filePath);

            var source = GetCommentOfZip(filePath);

            return new ComicMetadata
            {
                Version = "1.0",
                Title = title,
                Source = source.Length == 0 ? fileName : source,
                Tags = tags,
                System = new SystemInfo
                {
                    // 从文件系统获取时间信息
                    CreatedTime = fileInfo.CreationTime,
                    LastAccess = fileInfo.LastAccessTime,
                    ReadProgress = 0,
                    Rating = 0
                }
            };
        }
        public static string ComicNormalPath(string Key)
        {
            return Path.Combine(Configs.GetFilePath(), $"{Key}.zip");
        }
        public static string CalculateMD5(string input)
        {
            using (var md5 = MD5.Create())
            {
                // 对于中文字符，使用UTF-8编码
                byte[] inputBytes = Encoding.UTF8.GetBytes(input);
                byte[] hashBytes = md5.ComputeHash(inputBytes);

                StringBuilder sb = new StringBuilder();
                foreach (byte b in hashBytes)
                {
                    sb.Append(b.ToString("x2"));
                }

                return sb.ToString();
            }
        }

        // For paths that could be a link
        public static string GetFileRealPath(string path)
        {
            FileInfo sourceFile = new FileInfo(path);
            // Resolve the final, physical target of the file/link
            FileSystemInfo? finalTarget = sourceFile.ResolveLinkTarget(true);
            // Get the path string of the final target
            return finalTarget?.FullName ?? path;
        }
        public static BitmapImage ResizeImage(BitmapImage originalBitmap, int maxHeight, int maxWidth)
        {
            // 计算保持比例的缩放因子
            double widthRatio = (double)maxWidth / originalBitmap.PixelWidth;
            double heightRatio = (double)maxHeight / originalBitmap.PixelHeight;
            double scale = Math.Min(widthRatio, heightRatio);

            if (scale >= 1.0) return originalBitmap;

            var transform = new ScaleTransform(scale, scale);
            var transformedBitmap = new TransformedBitmap(originalBitmap, transform);

            var result = new BitmapImage();
            using (var stream = new MemoryStream())
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(transformedBitmap));
                encoder.Save(stream);
                stream.Position = 0;

                result.BeginInit();
                result.CacheOption = BitmapCacheOption.OnLoad;
                result.StreamSource = stream;
                result.EndInit();
                result.Freeze();
            }

            return result;
        }
        public static List<string> ParseTokens(string input, char[] delimiters)
        {
            if (string.IsNullOrWhiteSpace(input))
                return new List<string>();

            // 将分隔符转义用于正则表达式
            string escapedDelimiters = string.Join("", delimiters
                .Select(d => Regex.Escape(d.ToString())));

            // 匹配：引号内的内容（非贪婪）| 非分隔符的连续字符
            string pattern = @"
                ""([^""]*)""          # 双引号内的内容
                |
                '([^']*)'            # 单引号内的内容  
                |
                [^" + escapedDelimiters + @"]+  # 非分隔符的连续字符
            ";

            var matches = Regex.Matches(input, pattern,
                RegexOptions.IgnorePatternWhitespace);

            return matches
                .Cast<Match>()
                .Select(m => m.Value.Trim('"', '\'').Trim()) // 去除引号和空白
                .Where(token => !string.IsNullOrEmpty(token))
                .ToList();
        }

    }
}
