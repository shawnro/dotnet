// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable disable

namespace Microsoft.AspNetCore.Razor.Tasks
{
    public class AssemblyItem
    {
        public string Path { get; set; }

        public bool IsFrameworkReference { get; set; }

        public string AssemblyName { get; set; }
    }
}
