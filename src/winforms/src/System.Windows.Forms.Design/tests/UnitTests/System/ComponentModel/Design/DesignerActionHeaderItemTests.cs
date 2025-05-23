// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable disable

using System.Collections.Specialized;

namespace System.ComponentModel.Design.Tests;

public class DesignerActionHeaderItemTests
{
    [Theory]
    [InlineData("displayName", "category", "displayName")]
    [InlineData("displa(&a)yName", "cate(&a)gory", "displayName")]
    [InlineData("", "", "")]
    [InlineData(null, null, null)]
    public void DesignerActionItem_Ctor_String_String(string displayName, string category, string expectedDisplayName)
    {
        DesignerActionHeaderItem item = new(displayName, category);
        Assert.Equal(expectedDisplayName, item.DisplayName);
        Assert.Equal(category, item.Category);
        Assert.Null(item.Description);
        Assert.False(item.AllowAssociate);
        Assert.Empty(item.Properties);
        Assert.Same(item.Properties, item.Properties);
        Assert.IsType<HybridDictionary>(item.Properties);
        Assert.True(item.ShowInSourceView);
    }

    [Theory]
    [InlineData("displayName", "displayName")]
    [InlineData("displa(&a)yName", "displayName")]
    [InlineData("", "")]
    [InlineData(null, null)]
    public void DesignerActionItem_Ctor_String(string displayName, string expectedDisplayName)
    {
        DesignerActionHeaderItem item = new(displayName);
        Assert.Equal(expectedDisplayName, item.DisplayName);
        Assert.Equal(displayName, item.Category);
        Assert.Null(item.Description);
        Assert.False(item.AllowAssociate);
        Assert.Empty(item.Properties);
        Assert.Same(item.Properties, item.Properties);
        Assert.IsType<HybridDictionary>(item.Properties);
        Assert.True(item.ShowInSourceView);
    }
}
