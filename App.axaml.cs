using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;

namespace PlaybackPresent
{
   public partial class App : Application
   {
      public override void Initialize()
      {
         AvaloniaXamlLoader.Load(this);
      }

      public override void OnFrameworkInitializationCompleted()
      {
         // If you use CommunityToolkit, line below is needed to remove Avalonia data validation.
         // Without this line you will get duplicate validations from both Avalonia and CT
         BindingPlugins.DataValidators.RemoveAt(0);

         // Register all the services needed for the application to run
         var collection = new ServiceCollection();
         collection.AddCommonServices();

         // Creates a ServiceProvider containing services from the provided IServiceCollection
         var services = collection.BuildServiceProvider();

         //var vm = services.GetRequiredService<MainViewModel>();

         if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
         {
            desktop.MainWindow = new MainWindow();
         }

         base.OnFrameworkInitializationCompleted();
      }

      private void ExitBtn_Click(object? sender, System.EventArgs e)
      {
         if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
         {
         }
      }
   }
}