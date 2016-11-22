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

        public DateTime? NextActivationDate { get; private set; }

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

        public void OnCancellationTokenCancelled(CancellationToken token)
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
