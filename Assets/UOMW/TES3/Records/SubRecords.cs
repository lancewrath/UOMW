using ESMSharp.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ESMSharp.TES3.Records
{
    public class SubRecords
    {
        protected string _type = "NONE";
        protected uint _unknown = 0;
        public SubRecords() 
        {
        
        
        
        }
        public SubRecords(string type) 
        {
            _type = type;
        }

        public virtual void Deserialize(BetterReader reader, string name)
        {
            _type = name;
        }

    }




}
