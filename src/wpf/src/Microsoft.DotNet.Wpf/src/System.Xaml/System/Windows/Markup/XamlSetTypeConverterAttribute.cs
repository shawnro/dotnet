// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Windows.Markup
{
   [AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
    public sealed class XamlSetTypeConverterAttribute : Attribute
    {
        public XamlSetTypeConverterAttribute(string? xamlSetTypeConverterHandler)
        {
            XamlSetTypeConverterHandler = xamlSetTypeConverterHandler;
        }

        public string? XamlSetTypeConverterHandler { get; }
    }
}
