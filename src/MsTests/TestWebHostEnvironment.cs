using System;
using System.IO;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

namespace MsTests
{
    /// <summary>
    /// Minimal IWebHostEnvironment for the tests
    /// (Microsoft.AspNetCore.Hosting.Internal.HostingEnvironment is no longer public since ASP.NET Core 3.0).
    /// </summary>
    public class TestWebHostEnvironment : IWebHostEnvironment
    {
        public TestWebHostEnvironment()
        {
            ContentRootPath = AppContext.BaseDirectory;
            WebRootPath = Path.Combine(ContentRootPath, "wwwroot");
            ContentRootFileProvider = new NullFileProvider();
            WebRootFileProvider = new NullFileProvider();
        }

        public string ApplicationName { get; set; } = "MsTests";
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; }
        public IFileProvider ContentRootFileProvider { get; set; }
        public string WebRootPath { get; set; }
        public IFileProvider WebRootFileProvider { get; set; }
    }
}
