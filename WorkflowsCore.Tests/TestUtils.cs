using System;
using System.Threading.Tasks;

namespace WorkflowsCore.Tests
{
    public static class TestUtils
    {
        public static void DoAsync(Action action) => Task.Delay(1).ContinueWith(t => action());
    }
}
