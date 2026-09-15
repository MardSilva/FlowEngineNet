using Flow.Cli;

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += OnCancelKeyPress;
try
{
    return await FlowCliApplication.CreateDefault().RunAsync(args, Console.Out, Console.Error, cancellation.Token);
}
finally
{
    Console.CancelKeyPress -= OnCancelKeyPress;
}

void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs eventArgs)
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
}
