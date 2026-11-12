using System.Collections;
using System.Reflection;

namespace APS.SystemControl.Core
{
    public class Assemblies : IEnumerable<KeyValuePair<string, Assembly>>
    {
        private readonly Dictionary<string, Assembly> _assemblies;

        public Assemblies()
        {
            _assemblies = new Dictionary<string, Assembly>();
        }

        public IEnumerator<KeyValuePair<string, Assembly>> GetEnumerator()
        {
            return _assemblies.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }

        public void Add(string name, Assembly assembly)
        {
            _assemblies.Add(name, assembly);
        }

        public Assembly? this[string name]
        {
            get
            {
                try
                {
                    return _assemblies[name];
                }
                catch { return null; }
            }
        }
    }
}
