using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ComicViewer.Services
{
    public class ComicUtils
    {
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
