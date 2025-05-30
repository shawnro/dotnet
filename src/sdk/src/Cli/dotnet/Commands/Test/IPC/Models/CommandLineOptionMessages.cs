﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.DotNet.Cli.Commands.Test.IPC.Models;

internal sealed record CommandLineOptionMessage(string? Name, string? Description, bool? IsHidden, bool? IsBuiltIn);

internal sealed record CommandLineOptionMessages(string? ModulePath, CommandLineOptionMessage[]? CommandLineOptionMessageList) : IRequest;
