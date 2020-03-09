// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace NuGet.VisualStudio.OnlineEnvironments.Client
{
    [Guid(PackageGuidString)]
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    internal sealed class NuGetClientPackage : AsyncPackage
    {
        public const string PackageGuidString = "6431ADB1-9428-4173-A696-6DECA317034A";
    }
}
