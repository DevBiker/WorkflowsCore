using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace WorkflowsCore
{
    public class ActivationDatesManager
    {
        private readonly IDictionary<CancellationToken, DateTimeOffset> _activationDates =
            new Dictionary<CancellationToken, DateTimeOffset>();

        private DateTimeOffset? _nextActivationDate;

        public event EventHandler NextActivationDateChanged;

        public DateTimeOffset? NextActivationDate
        {
            get
            {
                return _nextActivationDate;
            }

            private set
            {
                _nextActivationDate = value;
                NextActivationDateChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public void AddActivationDate(CancellationToken token, DateTimeOffset date)
        {
            if (date == DateTimeOffset.MaxValue)
            {
                return;
            }

            DateTimeOffset cur;
            if (!_activationDates.TryGetValue(token, out cur))
            {
                _activationDates.Add(token, date);
            }
            else if (cur > date)
            {
                _activationDates[token] = date;
            }

            if (!NextActivationDate.HasValue || NextActivationDate > date)
            {
                NextActivationDate = date;
            }
        }

        public void OnCancellationTokenCanceled(CancellationToken token)
        {
            DateTimeOffset cur;
            if (!_activationDates.TryGetValue(token, out cur))
            {
                return;
            }

            _activationDates.Remove(token);
            if (NextActivationDate < cur)
            {
                return;
            }

            NextActivationDate = !_activationDates.Any() ? null : (DateTimeOffset?)_activationDates.Min(p => p.Value);
        }
    }
}
