// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Globalization;

namespace MS.Internal
{
    /// <summary>
    /// Exposes the CultureInfo for the culture the platform is localized to.
    /// </summary>
    internal static class PlatformCulture
    {
        /// <summary>
        /// Culture the platform is localized to.
        /// </summary>    
        public static CultureInfo Value
        {
            get 
            {
                // Get the UI Language from the string table
                string uiLanguage = SR.WPF_UILanguage;
                Invariant.Assert(!string.IsNullOrEmpty(uiLanguage), "No UILanguage was specified in stringtable.");
    
                // Return the CultureInfo for this UI language.
                return new CultureInfo(uiLanguage);
            }
        }
}
}
