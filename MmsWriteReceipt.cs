// IEDExplorer GPL-3.0-or-later
using System;
using System.Threading.Tasks;

namespace IEDExplorer
{
    /// <summary>
    /// Correlates a single MMS Write_Confirmed response to a temporary command
    /// element returned by the existing MMS engine. Only a positive response
    /// allows the next stage of SBOw; a timeout or reject never sends Operate.
    /// </summary>
    internal sealed class MmsWriteReceipt
    {
        private readonly TaskCompletionSource<bool> completion =
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        internal void Complete(bool accepted)
        {
            completion.TrySetResult(accepted);
        }

        internal async Task<bool> WaitForAckAsync(int timeoutMilliseconds)
        {
            Task finished = await Task.WhenAny(completion.Task, Task.Delay(timeoutMilliseconds));
            return object.ReferenceEquals(finished, completion.Task) && await completion.Task;
        }
    }
