namespace Flow.Cli;

internal interface ICliOperationObserver
{
    public void FileWritten(string path);

    public void Progress(CliOperationProgressUpdate update);
}
