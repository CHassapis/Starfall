using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Starlights.Integration.Drivers;
using Starlights.Integration.Drivers.Characters;
using Starlights.Integration.Drivers.Elements;
using Starlights.Integration.Extensions;

namespace Starlights.Integration;

public static class IntegrationHostBuilderExtensions
{
    extension(IntegrationHost host)
    {
        /// <summary>
        /// Creates a default <see cref="IntegrationHostBuilder"/> for the specified <see cref="IntegrationTestBase"/>.
        /// </summary>
        /// <remarks>
        /// This method configures the builder with the current assembly's driver assemblies and the test context from the provided test base.
        /// </remarks>
        public static IntegrationHostBuilder CreateDefaultBuilder(IntegrationTestBase testBase)
        {
            return new IntegrationHostBuilder()
                .WithDriverAssemblies(Assembly.GetExecutingAssembly(), typeof(ManageSpellsDriver).Assembly)
                .WithTestContext(testBase.TestContext)
                .RegisterDriverContext<ElementsDriverContext>()
                // the current character (the last one created); only the context, not that assembly's drivers
                .ConfigureServices(services => services.AddSingleton<CharactersDriverContext>());
        }
    }
}
