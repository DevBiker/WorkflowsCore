using System.Collections.Generic;
using System.Linq;

namespace WorkflowsCore
{
    public class NamedValues
    {
        private readonly Dictionary<string, object> _data;

        public NamedValues()
        {
            _data = new Dictionary<string, object>();
        }

        public NamedValues(IReadOnlyDictionary<string, object> data)
        {
            _data = data.ToDictionary(p => p.Key, p => p.Value);
        }

        public IReadOnlyDictionary<string, object> Data => _data;

        public T GetData<T>(string key)
        {
            object res;
            if (!_data.TryGetValue(key, out res))
            {
                return default(T);
            }

            return (T)res;
        }

        public void SetData<T>(string key, T value)
        {
            if (EqualityComparer<T>.Default.Equals(value, default(T)))
            {
                _data.Remove(key);
                return;
            }

            _data[key] = value;
        }

        public void SetData(IReadOnlyDictionary<string, object> newData)
        {
            foreach (var p in newData)
            {
                SetData(p.Key, p.Value);
            }
        }
    }
}
