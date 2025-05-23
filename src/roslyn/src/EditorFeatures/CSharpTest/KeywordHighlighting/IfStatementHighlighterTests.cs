﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable disable

using System;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.KeywordHighlighting;
using Microsoft.CodeAnalysis.Test.Utilities;
using Xunit;

namespace Microsoft.CodeAnalysis.Editor.CSharp.UnitTests.KeywordHighlighting;

[Trait(Traits.Feature, Traits.Features.KeywordHighlighting)]
public sealed class IfStatementHighlighterTests : AbstractCSharpKeywordHighlighterTests
{
    internal override Type GetHighlighterType()
        => typeof(IfStatementHighlighter);

    [Fact]
    public async Task TestIfStatementWithIfAndSingleElse1()
    {
        await TestAsync(
            """
            public class C
            {
                public void Goo()
                {
                    int a = 10;
                    {|Cursor:[|if|]|} (a < 5)
                    {
                        // blah
                    }
                    [|else|]
                    {
                        // blah
                    }
                }
            }
            """);
    }

    [Fact]
    public async Task TestIfStatementWithIfAndSingleElse2()
    {
        await TestAsync(
            """
            public class C
            {
                public void Goo()
                {
                    int a = 10;
                    [|if|] (a < 5)
                    {
                        // blah
                    }
                    {|Cursor:[|else|]|}
                    {
                        // blah
                    }
                }
            }
            """);
    }

    [Fact]
    public async Task TestIfStatementWithIfAndElseIfAndElse1()
    {
        await TestAsync(
            """
            public class C
            {
                public void Goo()
                {
                    int a = 10;
                    {|Cursor:[|if|]|} (a < 5)
                    {
                        // blah
                    }
                    [|else if|] (a == 10)
                    {
                        // blah
                    }
                    [|else|]
                    {
                        // blah
                    }
                }
            }
            """);
    }

    [Fact]
    public async Task TestIfStatementWithIfAndElseIfAndElse2()
    {
        await TestAsync(
            """
            public class C
            {
                public void Goo()
                {
                    int a = 10;
                    [|if|] (a < 5)
                    {
                        // blah
                    }
                    {|Cursor:[|else if|]|} (a == 10)
                    {
                        // blah
                    }
                    [|else|]
                    {
                        // blah
                    }
                }
            }
            """);
    }

    [Fact]
    public async Task TestIfStatementWithIfAndElseIfAndElse3()
    {
        await TestAsync(
            """
            public class C
            {
                public void Goo()
                {
                    int a = 10;
                    [|if|] (a < 5)
                    {
                        // blah
                    }
                    [|else if|] (a == 10)
                    {
                        // blah
                    }
                    {|Cursor:[|else|]|}
                    {
                        // blah
                    }
                }
            }
            """);
    }

    [Fact]
    public async Task TestIfStatementWithElseIfOnDifferentLines1()
    {
        await TestAsync(
            """
            public class C
            {
                public void Goo()
                {
                    int a = 10;
                    {|Cursor:[|if|]|} (a < 5)
                    {
                        // blah
                    }
                    [|else|]
                    [|if|] (a == 10)
                    {
                        // blah
                    }
                    [|else|]
                    {
                        // blah
                    }
                }
            }
            """);
    }

    [Fact]
    public async Task TestIfStatementWithElseIfOnDifferentLines2()
    {
        await TestAsync(
            """
            public class C
            {
                public void Goo()
                {
                    int a = 10;
                    [|if|] (a < 5)
                    {
                        // blah
                    }
                    {|Cursor:[|else|]|}
                    [|if|] (a == 10)
                    {
                        // blah
                    }
                    [|else|]
                    {
                        // blah
                    }
                }
            }
            """);
    }

    [Fact]
    public async Task TestIfStatementWithElseIfOnDifferentLines3()
    {
        await TestAsync(
            """
            public class C
            {
                public void Goo()
                {
                    int a = 10;
                    [|if|] (a < 5)
                    {
                        // blah
                    }
                    [|else|]
                    {|Cursor:[|if|]|} (a == 10)
                    {
                        // blah
                    }
                    [|else|]
                    {
                        // blah
                    }
                }
            }
            """);
    }

    [Fact]
    public async Task TestIfStatementWithElseIfOnDifferentLines4()
    {
        await TestAsync(
            """
            public class C
            {
                public void Goo()
                {
                    int a = 10;
                    [|if|] (a < 5)
                    {
                        // blah
                    }
                    [|else|]
                    [|if|] (a == 10)
                    {
                        // blah
                    }
                    {|Cursor:[|else|]|}
                    {
                        // blah
                    }
                }
            }
            """);
    }

    [Fact]
    public async Task TestIfStatementWithIfAndElseIfAndElseTouching1()
    {
        await TestAsync(
            """
            public class C
            {
                public void Goo()
                {
                    int a = 10;
                    {|Cursor:[|if|]|}(a < 5)
                    {
                        // blah
                    }
                    [|else if|](a == 10)
                    {
                        // blah
                    }
                    [|else|]{
                        // blah
                    }
                }
            }
            """);
    }

    [Fact]
    public async Task TestIfStatementWithIfAndElseIfAndElseTouching2()
    {
        await TestAsync(
            """
            public class C
            {
                public void Goo()
                {
                    int a = 10;
                    [|if|](a < 5)
                    {
                        // blah
                    }
                    {|Cursor:[|else if|]|}(a == 10)
                    {
                        // blah
                    }
                    [|else|]{
                        // blah
                    }
                }
            }
            """);
    }

    [Fact]
    public async Task TestIfStatementWithIfAndElseIfAndElseTouching3()
    {
        await TestAsync(
            """
            public class C
            {
                public void Goo()
                {
                    int a = 10;
                    [|if|](a < 5)
                    {
                        // blah
                    }
                    [|else if|](a == 10)
                    {
                        // blah
                    }
                    {|Cursor:[|else|]|}{
                        // blah
                    }
                }
            }
            """);
    }

    [Fact]
    public async Task TestExtraSpacesBetweenElseAndIf1()
    {
        await TestAsync(
            """
            public class C
            {
                public void Goo()
                {
                    int a = 10;
                    {|Cursor:[|if|]|} (a < 5)
                    {
                        // blah
                    }
                    [|else if|] (a == 10)
                    {
                        // blah
                    }
                    [|else|]
                    {
                        // blah
                    }
                }
            }
            """);
    }

    [Fact]
    public async Task TestExtraSpacesBetweenElseAndIf2()
    {
        await TestAsync(
            """
            public class C
            {
                public void Goo()
                {
                    int a = 10;
                    [|if|] (a < 5)
                    {
                        // blah
                    }
                    {|Cursor:[|else if|]|} (a == 10)
                    {
                        // blah
                    }
                    [|else|]
                    {
                        // blah
                    }
                }
            }
            """);
    }

    [Fact]
    public async Task TestExtraSpacesBetweenElseAndIf3()
    {
        await TestAsync(
            """
            public class C
            {
                public void Goo()
                {
                    int a = 10;
                    [|if|] (a < 5)
                    {
                        // blah
                    }
                    [|else if|] (a == 10)
                    {
                        // blah
                    }
                    {|Cursor:[|else|]|}
                    {
                        // blah
                    }
                }
            }
            """);
    }

    [Fact]
    public async Task TestCommentBetweenElseIf1()
    {
        await TestAsync(
            """
            public class C
            {
                public void Goo()
                {
                    int a = 10;
                    {|Cursor:[|if|]|} (a < 5)
                    {
                        // blah
                    }
                    [|else|] /* test */ [|if|] (a == 10)
                    {
                        // blah
                    }
                    [|else|]
                    {
                        // blah
                    }
                }
            }
            """);
    }

    [Fact]
    public async Task TestCommentBetweenElseIf2()
    {
        await TestAsync(
            """
            public class C
            {
                public void Goo()
                {
                    int a = 10;
                    [|if|] (a < 5)
                    {
                        // blah
                    }
                    {|Cursor:[|else|]|} /* test */ [|if|] (a == 10)
                    {
                        // blah
                    }
                    [|else|]
                    {
                        // blah
                    }
                }
            }
            """);
    }

    [Fact]
    public async Task TestCommentBetweenElseIf3()
    {
        await TestAsync(
            """
            public class C
            {
                public void Goo()
                {
                    int a = 10;
                    [|if|] (a < 5)
                    {
                        // blah
                    }
                    [|else|] /* test */ {|Cursor:[|if|]|} (a == 10)
                    {
                        // blah
                    }
                    [|else|]
                    {
                        // blah
                    }
                }
            }
            """);
    }

    [Fact]
    public async Task TestCommentBetweenElseIf4()
    {
        await TestAsync(
            """
            public class C
            {
                public void Goo()
                {
                    int a = 10;
                    [|if|] (a < 5)
                    {
                        // blah
                    }
                    [|else|] /* test */ [|if|] (a == 10)
                    {
                        // blah
                    }
                    {|Cursor:[|else|]|}
                    {
                        // blah
                    }
                }
            }
            """);
    }

    [Fact]
    public async Task TestNestedIfDoesNotHighlight1()
    {
        await TestAsync(
            """
            public class C
            {
                public void Goo()
                {
                    int a = 10;
                    int b = 15;
                    {|Cursor:[|if|]|} (a < 5)
                    {
                        // blah
                        if (b < 15)
                            b = 15;
                        else
                            b = 14;
                    }
                    [|else if|] (a == 10)
                    {
                        // blah
                    }
                    [|else|]
                    {
                        // blah
                    }
                }
            }
            """);
    }

    [Fact]
    public async Task TestNestedIfDoesNotHighlight2()
    {
        await TestAsync(
            """
            public class C
            {
                public void Goo()
                {
                    int a = 10;
                    int b = 15;
                    [|if|] (a < 5)
                    {
                        // blah
                        if (b < 15)
                            b = 15;
                        else
                            b = 14;
                    }
                    {|Cursor:[|else if|]|} (a == 10)
                    {
                        // blah
                    }
                    [|else|]
                    {
                        // blah
                    }
                }
            }
            """);
    }

    [Fact]
    public async Task TestNestedIfDoesNotHighlight3()
    {
        await TestAsync(
            """
            public class C
            {
                public void Goo()
                {
                    int a = 10;
                    int b = 15;
                    [|if|] (a < 5)
                    {
                        // blah
                        if (b < 15)
                            b = 15;
                        else
                            b = 14;
                    }
                    [|else if|] (a == 10)
                    {
                        // blah
                    }
                    {|Cursor:[|else|]|}
                    {
                        // blah
                    }
                }
            }
            """);
    }

    [Fact]
    public async Task TestExample1_1()
    {
        await TestAsync(
            """
            class C
            {
                void M()
                {
                    {|Cursor:[|if|]|} (x)
                    {
                        if (y)
                        {
                            F();
                        }
                        else if (z)
                        {
                            G();
                        }
                        else
                        {
                            H();
                        }
                    }
                }
            }
            """);
    }

    [Fact]
    public async Task TestExample2_1()
    {
        await TestAsync(
            """
            class C
            {
                void M()
                {
                    if (x)
                    {
                        {|Cursor:[|if|]|} (y)
                        {
                            F();
                        }
                        [|else if|] (z)
                        {
                            G();
                        }
                        [|else|]
                        {
                            H();
                        }
                    }
                }
            }
            """);
    }

    [Fact]
    public async Task TestExample2_2()
    {
        await TestAsync(
            """
            class C
            {
                void M()
                {
                    if (x)
                    {
                        [|if|] (y)
                        {
                            F();
                        }
                        {|Cursor:[|else if|]|} (z)
                        {
                            G();
                        }
                        [|else|]
                        {
                            H();
                        }
                    }
                }
            }
            """);
    }

    [Fact]
    public async Task TestExample2_3()
    {
        await TestAsync(
            """
            class C
            {
                void M()
                {
                    if (x)
                    {
                        [|if|] (y)
                        {
                            F();
                        }
                        [|else if|] (z)
                        {
                            G();
                        }
                        {|Cursor:[|else|]|}
                        {
                            H();
                        }
                    }
                }
            }
            """);
    }
}
