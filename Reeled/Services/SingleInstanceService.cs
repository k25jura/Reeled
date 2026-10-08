using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Reeled.Services;

public static class SingleInstanceService
{
    private const string PipeName = "Reeled_SingleInstance_Pipe_k25jura";
    private const string MutexName = "Reeled_SingleInstance_Mutex_k25jura";
    private static Mutex? _mutex;
    private static CancellationTokenSource? _pipeCts;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    public static bool TryRegisterSingleInstance(IReadOnlyList<string> filesToOpen)
    {
        try
        {
            _mutex = new Mutex(true, MutexName, out bool isFirstInstance);
            if (!isFirstInstance)
            {
                // Another instance is already running. Send files over named pipe.
                try
                {
                    using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                    client.Connect(1000);
                    using var writer = new StreamWriter(client) { AutoFlush = true };
                    foreach (var file in filesToOpen)
                    {
                        writer.WriteLine(file);
                    }
                }
                catch { }

                return false;
            }

            StartPipeServer();
            return true;
        }
        catch
        {
            // If mutex creation fails for any reason, proceed as first instance
            return true;
        }
    }

    private static void StartPipeServer()
    {
        _pipeCts = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            while (!_pipeCts.Token.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.In,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(_pipeCts.Token);
                    using var reader = new StreamReader(server);
                    var files = new List<string>();
                    string? line;
                    while ((line = await reader.ReadLineAsync()) != null)
                    {
                        if (!string.IsNullOrWhiteSpace(line) && File.Exists(line) && NavigationService.IsVideoFilePath(line))
                        {
                            files.Add(line);
                        }
                    }

                    if (files.Count > 0)
                    {
                        App.DispatcherQueue?.TryEnqueue(async () =>
                        {
                            BringToForeground();
                            var nav = App.GetService<INavigationService>();
                            await nav.OpenVideoFilesAsync(files);
                        });
                    }
                }
                catch when (_pipeCts.IsCancellationRequested)
                {
                    break;
                }
                catch
                {
                    await Task.Delay(500);
                }
            }
        });
    }

    public static void BringToForeground()
    {
        try
        {
            if (App.WindowHandle != IntPtr.Zero)
            {
                ShowWindow(App.WindowHandle, 9); // SW_RESTORE
                SetForegroundWindow(App.WindowHandle);
            }
        }
        catch { }
    }
}
