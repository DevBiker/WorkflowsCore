using System;
using System.Threading;
using Xunit;

namespace WorkflowsCore.Tests
{
    public class ActivationDatesManagerTests
    {
        private readonly ActivationDatesManager _manager = new ActivationDatesManager();

        [Fact]
        public void NextActivationDateShouldIgnoreMaxValue()
        {
            _manager.AddActivationDate(CancellationToken.None, DateTime.MaxValue);
            Assert.Null(_manager.NextActivationDate);
        }

        [Fact]
        public void NextActivationDateShouldBeSmallestForToken()
        {
            _manager.AddActivationDate(CancellationToken.None, new DateTime(2016, 11, 23));
            var min = new DateTime(2016, 11, 22);
            _manager.AddActivationDate(CancellationToken.None, min);
            _manager.AddActivationDate(CancellationToken.None, new DateTime(2016, 11, 24));

            Assert.Equal(min, _manager.NextActivationDate);
        }

        [Fact]
        public void NextActivationDateShouldBeSmallestAmongTokens()
        {
            var min = new DateTime(2016, 11, 22);
            _manager.AddActivationDate(CancellationToken.None, min);

            var cts = new CancellationTokenSource();
            _manager.AddActivationDate(cts.Token, new DateTime(2016, 11, 23));

            Assert.Equal(min, _manager.NextActivationDate);
        }

        [Fact]
        public void IfTokenIsCancelledItDatesShouldBeIgnored()
        {
            var expected = new DateTime(2016, 11, 23);
            _manager.AddActivationDate(CancellationToken.None, expected);

            var cts = new CancellationTokenSource();
            var min = new DateTime(2016, 11, 22);
            _manager.AddActivationDate(cts.Token, min);

            Assert.Equal(min, _manager.NextActivationDate);

            _manager.OnCancellationTokenCanceled(cts.Token);
            Assert.Equal(expected, _manager.NextActivationDate);
        }

        [Fact]
        public void IfAllTokensAreRemovedNextActivationDateShouldBeNull()
        {
            _manager.AddActivationDate(CancellationToken.None, new DateTime(2016, 11, 23));
            _manager.OnCancellationTokenCanceled(CancellationToken.None);

            Assert.Null(_manager.NextActivationDate);
        }

        [Fact]
        public void OnCancellationTokenCancelledMayBeCalledManyTimesForTheSameToken()
        {
            _manager.AddActivationDate(CancellationToken.None, new DateTime(2016, 11, 23));
            _manager.OnCancellationTokenCanceled(CancellationToken.None);
            _manager.OnCancellationTokenCanceled(CancellationToken.None);
        }

        [Fact]
        public void IfNextActivationDateIsChangedThenNextActivationDateChangedEventShouldBeFired()
        {
            var counter = 0;
            _manager.NextActivationDateChanged += (sender, args) => ++counter;
            _manager.AddActivationDate(CancellationToken.None, new DateTime(2016, 11, 23));
            Assert.Equal(1, counter);

            _manager.AddActivationDate(CancellationToken.None, new DateTime(2016, 11, 24));
            Assert.Equal(1, counter);

            _manager.AddActivationDate(CancellationToken.None, new DateTime(2016, 11, 22));
            Assert.Equal(2, counter);

            _manager.OnCancellationTokenCanceled(CancellationToken.None);
            Assert.Equal(3, counter);
        }
    }
}
