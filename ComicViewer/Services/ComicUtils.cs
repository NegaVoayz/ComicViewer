using ComicViewer.Models;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media;

namespace ComicViewer.Services
{
    public class ComicUtils
    {
        public static string GetCombinedName(string name, IEnumerable<string> tags)
        {
            if (!tags.Any())
            {
                return name.Trim();
            }

            // 构建标签字符串，每个标签用ASCII中括号包裹
            var tagStrings = tags.Select(tag => $"[{tag}]");

            // 用空格连接所有部分
            string result = $"{name} {string.Join(" ", tagStrings)}";

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

            // 定义所有可能的括号类型
            char[] openingBrackets = { '[', '【', '〔', '［' };  // 左括号：半角、全角、中文、其他全角
            char[] closingBrackets = { ']', '】', '〕', '］' };  // 右括号：半角、全角、中文、其他全角

            bool insideBrackets = false;
            int bracketDepth = 0;
            char expectedClosingBracket = '\0';

            for (int i = 0; i < input.Length; i++)
            {
                char currentChar = input[i];

                // 检查是否是左括号
                int openingIndex = Array.IndexOf(openingBrackets, currentChar);
                if (openingIndex != -1 && !insideBrackets)
                {
                    insideBrackets = true;
                    bracketDepth = 1;
                    expectedClosingBracket = closingBrackets[openingIndex];
                    continue;
                }

                // 检查是否是右括号
                if (insideBrackets && currentChar == expectedClosingBracket)
                {
                    bracketDepth--;
                    if (bracketDepth == 0)
                    {
                        // 找到完整的最外层括号对
                        string content = currentBracketContent.ToString().Trim();
                        if (!string.IsNullOrEmpty(content))
                        {
                            extracted.Add(content);
                        }

                        currentBracketContent.Clear();
                        insideBrackets = false;
                        continue;
                    }
                }

                // 检查嵌套的左括号（理论上不会出现，但为了健壮性处理）
                if (insideBrackets)
                {
                    int nestedOpeningIndex = Array.IndexOf(openingBrackets, currentChar);
                    if (nestedOpeningIndex != -1)
                    {
                        bracketDepth++;
                        currentBracketContent.Append(currentChar);
                        continue;
                    }
                }

                // 处理字符
                if (insideBrackets)
                {
                    currentBracketContent.Append(currentChar);
                }
                else
                {
                    remaining.Append(currentChar);
                }
            }

            // 如果最后还在括号内，将内容添加到剩余字符串中
            if (insideBrackets)
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

            return new ComicMetadata
            {
                Version = "1.0",
                Title = title,
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
            return System.IO.Path.Combine(Configs.GetFilePath(), $"{Key}.zip");
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
        public static bool IsCrossDeviceException(IOException ex)
        {
            const int ERROR_NOT_SAME_DEVICE = 0x11;        // 17 (Windows)
            const int ERROR_INVALID_PARAMETER = 0x57;      // 87 (Windows - 某些情况)
            const int EXDEV = 18;                          // Unix/Linux/macOS

            // 获取 HResult（Windows）或 ErrorCode（Unix）
            int errorCode = ex.HResult & 0xFFFF;

            // 检查常见错误码
            return errorCode == ERROR_NOT_SAME_DEVICE ||
                   errorCode == ERROR_INVALID_PARAMETER ||
                   errorCode == EXDEV;
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
    }
}
