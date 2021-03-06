﻿// Licensed to the .NET Foundation under one or more agreements. The .NET Foundation licenses this file to you under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.ProjectSystem.Debug;

namespace Microsoft.VisualStudio.ProjectSystem.Properties
{
    /// <summary>
    /// <para>
    /// An implementation of <see cref="IInterceptingPropertyValueProvider"/> that
    /// simplifies reading and writing property values in the launch settings.
    /// </para>
    /// <para>
    /// Derived types are only responsible for reading values from a provided <see cref="ILaunchSettings"/>
    /// and/or writing values to a <see cref="IWritableLaunchSettings"/>; this type
    /// handles acquiring the settings in the first place and storing them after updates
    /// have been made.
    /// </para>
    /// <para>
    /// Concrete implementations of this type, as with all implementations of <see cref="IInterceptingPropertyValueProvider"/>,
    /// should be tagged with the <see cref="ExportInterceptingPropertyValueProviderAttribute"/>.
    /// </para>
    /// </summary>
    public abstract class LaunchSettingsValueProviderBase : InterceptingPropertyValueProviderBase
    {
        private readonly UnconfiguredProject _project;
        private readonly ILaunchSettingsProvider _launchSettingsProvider;
        private readonly IProjectThreadingService _projectThreadingService;

        public LaunchSettingsValueProviderBase(UnconfiguredProject project, ILaunchSettingsProvider launchSettingsProvider, IProjectThreadingService projectThreadingService)
        {
            _project = project;
            _launchSettingsProvider = launchSettingsProvider;
            _projectThreadingService = projectThreadingService;
        }

        public override Task<string> OnGetEvaluatedPropertyValueAsync(string propertyName, string evaluatedPropertyValue, IProjectProperties defaultProperties)
        {
            return GetPropertyValueAsync(propertyName);
        }

        public override Task<string> OnGetUnevaluatedPropertyValueAsync(string propertyName, string unevaluatedPropertyValue, IProjectProperties defaultProperties)
        {
            return GetPropertyValueAsync(propertyName);
        }

        public override Task<string?> OnSetPropertyValueAsync(string propertyName, string unevaluatedPropertyValue, IProjectProperties defaultProperties, IReadOnlyDictionary<string, string>? dimensionalConditions = null)
        {
            _projectThreadingService.RunAndForget(async () =>
            {
                ILaunchSettings launchSettings = await _launchSettingsProvider.WaitForFirstSnapshot(Timeout.Infinite);

                var writableLaunchSettings = launchSettings.ToWritableLaunchSettings();
                if (SetPropertyValue(propertyName, unevaluatedPropertyValue, writableLaunchSettings))
                {
                    await _launchSettingsProvider.UpdateAndSaveSettingsAsync(writableLaunchSettings.ToLaunchSettings());
                }
            },
options: ForkOptions.HideLocks,
unconfiguredProject: _project);

            // We've intercepted the "set" operation and redirected it to the launch settings.
            // Return "null" to indicate that the value should _not_ be set in the project file
            // as well.
            return Task.FromResult<string?>(null);
        }

        private async Task<string> GetPropertyValueAsync(string propertyName)
        {
            ILaunchSettings launchSettings = await _launchSettingsProvider.WaitForFirstSnapshot(Timeout.Infinite);

            return GetPropertyValue(propertyName, launchSettings) ?? string.Empty;
        }

        /// <summary>
        /// Retrieves the property specified by <paramref name="propertyName"/> from the
        /// given <see cref="ILaunchSettings"/>.
        /// </summary>
        /// <returns>
        /// The value of the property if it is found in the <paramref name="launchSettings"/>;
        /// otherwise a default value or <c>null</c> if there is no applicable default.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown if the given <paramref name="propertyName"/> is not known (that is, it is
        /// not declared in the implementor's <see cref="ExportInterceptingPropertyValueProviderAttribute"/>).
        /// </exception>
        public abstract string? GetPropertyValue(string propertyName, ILaunchSettings launchSettings);

        /// <summary>
        /// Sets the property specified by <paramref name="propertyName"/> to <paramref name="value"/>
        /// in the given <see cref="IWritableLaunchSettings"/>.
        /// </summary>
        /// <returns><c>true</c> if the <paramref name="launchSettings"/> were updated;
        /// otherwise <c>false</c>.</returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown if the given <paramref name="propertyName"/> is not known (that is, it is
        /// not declared in the implementor's <see cref="ExportInterceptingPropertyValueProviderAttribute"/>).
        /// </exception>
        public abstract bool SetPropertyValue(string propertyName, string value, IWritableLaunchSettings launchSettings);
    }
}
