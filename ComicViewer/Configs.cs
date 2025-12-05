using ComicViewer.Services;
using Microsoft.Extensions.Configuration;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ComicViewer
{
    public class Configs
    {
        private static readonly IConfiguration configure = new ConfigurationBuilder()
                                //设置配置项的根目录
                                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                                //添加当前目录下的Json文件
                                //optional=配置项是否可选，false时如果
                                //reloadOnChange配置文件改变时，是否重新读取
                                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                                //生成配置项
                                .Build();
        public static IConfiguration Get()
        {
            return configure;
        }
        public static string GetFilePath()
        {
            return configure.GetRequiredSection("comic_path").Value ?? "D:\\comics";
        }
        public static bool SetFilePath(string newPath)
        {
            newPath = ComicUtils.GetFileRealPath(newPath);
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
            // 读取现有的配置文件内容
            var jsonContent = File.ReadAllText(path);
            var jsonObject = JsonSerializer.Deserialize<JsonObject>(jsonContent);

            if (jsonObject == null)
            {
                throw new Exception("配置文件格式错误");
            }

            // 更新 comic_path 值
            jsonObject["comic_path"] = newPath;

            // 保存回文件
            var options = new JsonSerializerOptions
            {
                WriteIndented = true // 保持缩进格式
            };

            var updatedJson = JsonSerializer.Serialize(jsonObject, options);
            File.WriteAllText(path, updatedJson);

            return true;
        }
    }
}
