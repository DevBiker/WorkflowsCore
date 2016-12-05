using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace WorkflowsCore
{
    public class ActivationDatesManager
    {
        private readonly IDictionary<CancellationToken, DateTime> _activationDates =
            new Dictionary<CancellationToken, DateTime>();

        private DateTime? _nextActivationDate;

        public event EventHandler NextActivationDateChanged;

        public DateTime? NextActivationDate
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

        public void AddActivationDate(CancellationToken token, DateTime date)
        {
            DateTime cur;
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
            DateTime cur;
            if (!_activationDates.TryGetValue(token, out cur))
            {
                return;
            }

            _activationDates.Remove(token);
            if (NextActivationDate < cur)
            {
                return;
            }

            NextActivationDate = !_activationDates.Any() ? null : (DateTime?)_activationDates.Min(p => p.Value);
        }
    }
}
