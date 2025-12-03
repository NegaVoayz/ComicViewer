using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
        //private static readonly string filepath = "E:\\comics";
        public static IConfiguration Get()
        {
            return configure;
        }
        public static string GetFilePath()
        {
            return configure.GetRequiredSection("comic_path").Value ?? "D:\\comics";
        }
    }
}
