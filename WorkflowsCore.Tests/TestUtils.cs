using System;
using System.Threading.Tasks;

namespace WorkflowsCore.Tests
{
    public static class TestUtils
    {
        public static void DoAsync(Action action, int delay = 1) => Task.Delay(delay).ContinueWith(t => action());
    }
}
