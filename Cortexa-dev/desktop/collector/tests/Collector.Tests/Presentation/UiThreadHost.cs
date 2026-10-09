using System.Windows.Threading;

namespace Collector.Tests.Presentation;

internal static class UiThreadHost
{
    public static Task RunAsync(Func<Task> body)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => Pump(body, completion));
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static void Pump(Func<Task> body, TaskCompletionSource completion)
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
        dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(async () => await Execute(body, completion, dispatcher)));
        Dispatcher.Run();
    }

    private static async Task Execute(Func<Task> body, TaskCompletionSource completion, Dispatcher dispatcher)
    {
        try
        {
            await body();
            completion.SetResult();
        }
        catch (Exception ex)
        {
            completion.SetException(ex);
        }
        finally
        {
            dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
        }
    }
}
