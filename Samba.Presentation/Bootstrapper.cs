﻿using System;
using System.ComponentModel.Composition.Hosting;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using Microsoft.Practices.Prism.Events;
using Microsoft.Practices.Prism.MefExtensions;
using Microsoft.Practices.ServiceLocation;
using Samba.Infrastructure.Settings;
using Samba.Localization.Engine;
using Samba.Presentation.Common;
using Samba.Presentation.Common.Services;
using Samba.Presentation.ViewModels;
using Samba.Services;

namespace Samba.Presentation
{
    public class Bootstrapper : MefBootstrapper
    {
        static readonly Mutex Mutex = new Mutex(true, "{aa77c8c4-b8c1-4e61-908f-48c6fb65227d}");

        protected override DependencyObject CreateShell()
        {
            return Container.GetExportedValue<Shell>();
        }

        protected override void ConfigureAggregateCatalog()
        {
            base.ConfigureAggregateCatalog();
            var path = System.IO.Path.GetDirectoryName(Application.ResourceAssembly.Location);
            if (path != null)
            {
                AggregateCatalog.Catalogs.Add(new DirectoryCatalog(path, "Samba.Login*"));
                AggregateCatalog.Catalogs.Add(new DirectoryCatalog(path, "Samba.Modules*"));
                AggregateCatalog.Catalogs.Add(new DirectoryCatalog(path, "Samba.Presentation*"));
                AggregateCatalog.Catalogs.Add(new DirectoryCatalog(path, "Samba.Services.dll"));
            }
            LocalSettings.AppPath = path;
        }

        protected override void InitializeModules()
        {
            base.InitializeModules();
            var moduleInitializationService = ServiceLocator.Current.GetInstance<IModuleInitializationService>();
            moduleInitializationService.Initialize();
        }

        protected override void InitializeShell()
        {
            if (!Mutex.WaitOne(TimeSpan.Zero, true))
            {
                NativeWin32.PostMessage((IntPtr)NativeWin32.HWND_BROADCAST, NativeWin32.WM_SHOWSAMBAPOS, IntPtr.Zero, IntPtr.Zero);
                Environment.Exit(1);
            }

            LocalizeDictionary.ChangeLanguage(LocalSettings.CurrentLanguage);

            InteractionService.UserIntraction = ServiceLocator.Current.GetInstance<IUserInteraction>();
            InteractionService.UserIntraction.ToggleSplashScreen();

            AppServices.MainDispatcher = Application.Current.Dispatcher;

            AppServices.MessagingService.RegisterMessageListener(new MessageListener());

            if (LocalSettings.StartMessagingClient)
                AppServices.MessagingService.StartMessagingClient();

            GenericRuleRegistator.RegisterOnce();

            PresentationServices.Initialize();

            base.InitializeShell();

            try
            {
                var creationService = new DataCreationService();
                creationService.CreateData();
            }
            catch (Exception e)
            {
                if (!string.IsNullOrEmpty(LocalSettings.ConnectionString))
                {
                    var connectionString =
                        InteractionService.UserIntraction.GetStringFromUser(
                        "Connection String",
                        "Şu anki bağlantı ayarları ile veri tabanına bağlanılamıyor. Lütfen aşağıdaki bağlantı bilgisini kontrol ederek tekrar deneyiniz.\r\r" +
                        "Hata Mesajı:\r" + e.Message,
                        LocalSettings.ConnectionString);

                    var cs = String.Join(" ", connectionString);

                    if (!string.IsNullOrEmpty(cs))
                        LocalSettings.ConnectionString = cs.Trim();

                    AppServices.LogError(e, "Programı yeniden başlatınız. Mevcut problem log dosyasına kaydedildi.");
                }
                else
                {
                    AppServices.LogError(e);
                    LocalSettings.ConnectionString = "";
                }
                LocalSettings.SaveSettings();
                Environment.Exit(1);
            }

            Application.Current.MainWindow = (Shell)Shell;
            Application.Current.MainWindow.Show();
            EventServiceFactory.EventService.PublishEvent(EventTopicNames.ShellInitialized);
            //AppServices.CurrentLoggedInUser.PublishEvent(EventTopicNames.UserLoggedOut);
            InteractionService.UserIntraction.ToggleSplashScreen();
            ServiceLocator.Current.GetInstance<ITriggerService>().UpdateCronObjects();
            RuleExecutor.NotifyEvent(RuleEventNames.ApplicationStarted, new { });
        }
    }
}
