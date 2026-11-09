using System;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WebApi.Controllers;

namespace MsTests
{
    /// <summary>
    /// The API description (swagger) is generated for every action. Swashbuckle refuses an action whose own parameter is a file
    /// marked with [FromForm] (it answers 500 for the whole description); a file travels as a property of a form class.
    /// </summary>
    [TestClass]
    public class ApiDescriptionGuardTests
    {
        [TestMethod]
        public void NoActionTakesAFormFileAsItsOwnFromFormParameter()
        {
            var bad = typeof(DocumentsController).Assembly.GetTypes()
                .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                .SelectMany(m => m.GetParameters().Where(p =>
                        (p.ParameterType == typeof(IFormFile) || typeof(System.Collections.Generic.IEnumerable<IFormFile>).IsAssignableFrom(p.ParameterType))
                        && p.GetCustomAttributes(typeof(FromFormAttribute), false).Any())
                    .Select(p => m.DeclaringType.Name + "." + m.Name + "(" + p.Name + ")"))
                .ToList();
            Assert.AreEqual(0, bad.Count, "[FromForm] with IFormFile as a parameter breaks the API description: " + string.Join(", ", bad));
        }
    }
}
