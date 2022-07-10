﻿using Microsoft.AspNetCore;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.FileProviders;
using Newtonsoft.Json.Serialization;
using NLog.Web;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using YiSha.Admin.Web.Controllers;
using YiSha.Util;
using YiSha.Util.Model;

namespace YiSha.Admin.Web
{
    /// <summary>
    /// 程序入口
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// 程序入口
        /// </summary>
        /// <param name="args"></param>
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.ConfigureServices();
            var app = builder.Build();
            app.Configure();
            app.Run();
        }

        /// <summary>
        /// 该方法通过运行时调用
        /// 使用此方法将服务添加到容器中
        /// </summary>
        /// <param name="services">服务</param>
        public static void ConfigureServices(this IServiceCollection services)
        {
            //添加 Razor 页面的 Razor 运行时编译
            services.AddRazorPages().AddRazorRuntimeCompilation();
            //添加单例
            services.AddSingleton(HtmlEncoder.Create(UnicodeRanges.All));
            services.AddControllersWithViews(options =>
            {
                options.Filters.Add<GlobalExceptionFilter>();
                options.ModelMetadataDetailsProviders.Add(new ModelBindingMetadataProvider());
            }).AddNewtonsoftJson(options =>
            {
                //返回数据首字母不小写，CamelCasePropertyNamesContractResolver是小写
                options.SerializerSettings.ContractResolver = new DefaultContractResolver();
            });
            //启用缓存功能
            services.AddMemoryCache();
            //启动 Session
            services.AddSession(options =>
            {
                options.Cookie.Name = ".AspNetCore.Session";
                options.IdleTimeout = TimeSpan.FromDays(7);//设置Session的过期时间
                options.Cookie.HttpOnly = true;//设置在浏览器不能通过js获得该Cookie的值
                options.Cookie.IsEssential = true;
            });
            //
            services.Configure<CookiePolicyOptions>(options =>
            {
                //此 lambda 确定给定请求是否需要用户对非必要 cookie 的同意
                options.CheckConsentNeeded = context => true;
                options.MinimumSameSitePolicy = SameSiteMode.None;
            });
            //添加 Options 模式
            services.AddOptions();
            //添加 MVC
            services.AddMvc();
            //添加 HttpContext 存取器 
            services.AddHttpContextAccessor();
            //启动数据保护服务
            var directoryInfo = new DirectoryInfo($"{GlobalConstant.GetRunPath}{Path.DirectorySeparatorChar}DataProtection");
            services.AddDataProtection().PersistKeysToFileSystem(directoryInfo);
            //注册Encoding
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            //
            GlobalContext.Services = services;
        }

        /// <summary>
        /// 该方法通过运行时调用
        /// 使用此方法配置HTTP请求流水线
        /// </summary>
        /// <param name="app">应用</param>
        public static void Configure(this WebApplication app)
        {
            //配置对象
            GlobalContext.Configuration = app.Configuration;
            //系统配置
            GlobalContext.SystemConfig = app.Configuration.GetSection("SystemConfig").Get<SystemConfig>();
            //服务提供商
            GlobalContext.ServiceProvider = app.Services;
            //主机环境
            GlobalContext.HostingEnvironment = app.Environment;
            GlobalContext.LogWhenStart(app.Environment);
            //判断运行模式
            if (app.Environment.IsDevelopment())
            {
                GlobalContext.SystemConfig.Debug = true;
                //开发环境展示错误堆栈页
                app.UseDeveloperExceptionPage();
            }
            else
            {
                //正式环境自定义错误页
                app.UseExceptionHandler("/Help/Error");
                //捕获全局的请求
                app.Use(async (context, next) =>
                {
                    await next();
                    //401 错误
                    if (context.Response.StatusCode == 401)
                    {
                        context.Request.Path = "/Admin/Index";
                        await next();
                    }
                    //404 错误
                    if (context.Response.StatusCode == 404)
                    {
                        context.Request.Path = "/Help/Error";
                        await next();
                    }
                    //500 错误
                    if (context.Response.StatusCode == 500)
                    {
                        context.Request.Path = "/Help/Error";
                        await next();
                    }
                });
            }
            //
            if (!string.IsNullOrEmpty(GlobalContext.SystemConfig.VirtualDirectory))
            {
                //让 Pathbase 中间件成为第一个处理请求的中间件， 才能正确的模拟虚拟路径
                app.UsePathBase(new PathString(GlobalContext.SystemConfig.VirtualDirectory));
            }
            //静态目录
            app.UseStaticFiles(new StaticFileOptions
            {
                OnPrepareResponse = GlobalContext.SetCacheControl
            });
            //自定义静态目录
            string resource = Path.Combine(app.Environment.ContentRootPath, "Resource");
            FileHelper.CreateDirectory(resource);
            app.UseStaticFiles(new StaticFileOptions
            {
                RequestPath = "/Resource",
                FileProvider = new PhysicalFileProvider(resource),
                OnPrepareResponse = GlobalContext.SetCacheControl
            });
            //用户 Session
            app.UseSession();
            //用户路由
            app.UseRouting();
            //用户授权
            app.UseAuthorization();
            //用户默认路由
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllerRoute(
                    name: "areas",
                    pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}");
                endpoints.MapControllerRoute(
                    name: "default",
                    pattern: "{controller=Home}/{action=Index}/{id?}");
            });
        }

        public static IWebHostBuilder CreateWebHostBuilder(string[] args) =>
        WebHost.CreateDefaultBuilder(args)
               .UseUrls("http://*:5000")
               //.UseStartup<Startup>()
               .ConfigureLogging(logging =>
               {
                   logging.ClearProviders();
                   logging.SetMinimumLevel(LogLevel.Trace);
               }).UseNLog();
    }
}
