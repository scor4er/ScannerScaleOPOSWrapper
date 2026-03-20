using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Scanner_Scale_OPOS_Wrapper
{
    internal class Constants
    {
        internal const string DefaultPipeName = "ScannerScaleOPOSPipe";
        internal const RuntimeMode DefaultRuntimeMode = RuntimeMode.OPOS;

        internal enum RuntimeMode
        {
            OPOS,
            EMULATOR,
        }

        public enum MessageType
        {
            normal,
            scale_error,
            scanner_error,
            namedPipes_error,
            consoleOnly,
            ini,
            misc,
        }
    }
}
