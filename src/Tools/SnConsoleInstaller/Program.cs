﻿using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using SenseNet.Configuration;
using SenseNet.ContentRepository;
using SenseNet.ContentRepository.Storage.Data.MsSqlClient;
using SenseNet.Diagnostics;
using SenseNet.Extensions.DependencyInjection;
using SenseNet.Security.EFCSecurityStore;
using SenseNet.Services.Core.Install;
using Installer = SenseNet.Packaging.Installer;

namespace SnConsoleInstaller
{
    class Program
    {
        static void Main(string[] args)
        {
            IConfiguration config = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", true, true)
                .AddUserSecrets<Program>()
                .Build();

            var builder = new RepositoryBuilder()
                .SetConsole(Console.Out)
                .UseLogger(new SnFileSystemEventLogger())
                .UseTracer(new SnFileSystemTracer())
                .UseConfiguration(config)
                .UseDataProvider(new MsSqlDataProvider())
                .UseSecurityDataProvider(
                    new EFCSecurityDataProvider(connectionString: ConnectionStrings.ConnectionString))
                .UseLucene29LocalSearchEngine(Path.Combine(Environment.CurrentDirectory, "App_Data", "LocalIndex")) as RepositoryBuilder;

            new Installer(builder)
                .InstallSenseNet();
        }
    }
}
