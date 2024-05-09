using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KVMClient.Core.VNC
{
    /// <summary>
    /// Supported authentication methods.
    /// </summary>
    public enum AuthenticationMethod
    {
        /// <summary>
        /// No authentication is performed.
        /// </summary>
        None = 1,

        /// <summary>
        /// A password is used.
        /// </summary>
        Password = 2,
        /// <summary>
        /// SuperMicro
        /// </summary>
        SuperMicro = 16,
    }
}
