﻿// <copyright file="Registry.cs" company="Stubble Authors">
// Copyright (c) Stubble Authors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Stubble.Core.Classes.Loaders;
using Stubble.Core.Classes.Tokens;
using Stubble.Core.Helpers;
using Stubble.Core.Interfaces;

namespace Stubble.Core.Classes
{
    /// <summary>
    /// A class holding the instance data for a Stubble Renderer
    /// </summary>
    public sealed class Registry
    {
        private static readonly string[] DefaultTokenTypes = { @"\/", "=", @"\{", "!" };
        private static readonly string[] ReservedTokens = { "name", "text" }; // Name and text are used internally for tokens so must exist

        /// <summary>
        /// Initializes a new instance of the <see cref="Registry"/> class
        /// with default <see cref="RegistrySettings"/>
        /// </summary>
        public Registry()
            : this(default(RegistrySettings))
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Registry"/> class
        /// with given <see cref="RegistrySettings"/>
        /// </summary>
        /// <param name="settings">The registry settings to initalise the Registry with</param>
        public Registry(RegistrySettings settings)
        {
            SetValueGetters(settings.ValueGetters);
            SetTokenGetters(settings.TokenGetters);
            SetTruthyChecks(settings.TruthyChecks);
            SetEnumerationConverters(settings.EnumerationConverters);
            SetTemplateLoader(settings.TemplateLoader);
            SetPartialTemplateLoader(settings.PartialTemplateLoader);
            SetTokenMatchRegex();
            MaxRecursionDepth = settings.MaxRecursionDepth ?? 256;
            RenderSettings = settings.RenderSettings ?? RenderSettings.GetDefaultRenderSettings();
        }

        /// <summary>
        /// Gets a readonly dictionary of Value Getter functions
        /// </summary>
        public IReadOnlyDictionary<Type, Func<object, string, object>> ValueGetters { get; private set; }

        /// <summary>
        /// Gets a readonly dictionary of Token Getter functions
        /// </summary>
        public IReadOnlyDictionary<string, Func<string, Tags, ParserOutput>> TokenGetters { get; private set; }

        /// <summary>
        /// Gets a readonly list of Truthy Checks
        /// </summary>
        public IReadOnlyList<Func<object, bool?>> TruthyChecks { get; private set; }

        /// <summary>
        /// Gets a readonly dictionary of EnumerationConverters
        /// </summary>
        public IReadOnlyDictionary<Type, Func<object, IEnumerable>> EnumerationConverters { get; private set; }

        /// <summary>
        /// Gets the template loader for the Stubble instance
        /// </summary>
        public IStubbleLoader TemplateLoader { get; private set; }

        /// <summary>
        /// Gets the partial template loader for the Stubble instance
        /// </summary>
        public IStubbleLoader PartialTemplateLoader { get; private set; }

        /// <summary>
        /// Gets the max recursion depth for the render call
        /// </summary>
        public int MaxRecursionDepth { get; private set; }

        /// <summary>
        /// Gets the <see cref="RenderSettings"/> for the Stubble instance
        /// </summary>
        public RenderSettings RenderSettings { get; private set; }

        /// <summary>
        /// Gets the generated Token match regex
        /// </summary>
        internal Regex TokenMatchRegex { get; private set; }

        private void SetValueGetters(IDictionary<Type, Func<object, string, object>> valueGetters)
        {
            if (valueGetters != null)
            {
                var mergedGetters = RegistryDefaults.DefaultValueGetters.MergeLeft(valueGetters);

                mergedGetters = mergedGetters
                    .OrderBy(x => x.Key, TypeBySubclassAndAssignableImpl.TypeBySubclassAndAssignable())
                    .ToDictionary(item => item.Key, item => item.Value);

                ValueGetters = new ReadOnlyDictionary<Type, Func<object, string, object>>(mergedGetters);
            }
            else
            {
                ValueGetters = new ReadOnlyDictionary<Type, Func<object, string, object>>(RegistryDefaults.DefaultValueGetters);
            }
        }

        private void SetTokenGetters(IDictionary<string, Func<string, Tags, ParserOutput>> tokenGetters)
        {
            if (tokenGetters != null)
            {
                var mergedGetters = RegistryDefaults.DefaultTokenGetters.MergeLeft(tokenGetters);

                TokenGetters = new ReadOnlyDictionary<string, Func<string, Tags, ParserOutput>>(mergedGetters);
            }
            else
            {
                TokenGetters = new ReadOnlyDictionary<string, Func<string, Tags, ParserOutput>>(RegistryDefaults.DefaultTokenGetters);
            }
        }

        private void SetTruthyChecks(IReadOnlyList<Func<object, bool?>> truthyChecks)
        {
            TruthyChecks = truthyChecks ?? new List<Func<object, bool?>>();
        }

        private void SetEnumerationConverters(IDictionary<Type, Func<object, IEnumerable>> enumerationConverters)
        {
            if (enumerationConverters != null)
            {
                var mergedGetters = RegistryDefaults.DefaultEnumerationConverters.MergeLeft(enumerationConverters);
                EnumerationConverters = new ReadOnlyDictionary<Type, Func<object, IEnumerable>>(mergedGetters);
            }
            else
            {
                EnumerationConverters = new ReadOnlyDictionary<Type, Func<object, IEnumerable>>(RegistryDefaults.DefaultEnumerationConverters);
            }
        }

        private void SetTemplateLoader(IStubbleLoader loader)
        {
            TemplateLoader = loader ?? new StringLoader();
        }

        private void SetPartialTemplateLoader(IStubbleLoader loader)
        {
            PartialTemplateLoader = loader;
        }

        private void SetTokenMatchRegex()
        {
            TokenMatchRegex = new Regex(
                string.Join("|", TokenGetters.Where(s => !ReservedTokens.Contains(s.Key))
                                        .Select(s => Parser.EscapeRegexExpression(s.Key))
                                        .Concat(DefaultTokenTypes)));
        }

        private static class RegistryDefaults
        {
            public static readonly IDictionary<Type, Func<object, string, object>> DefaultValueGetters = new Dictionary<Type, Func<object, string, object>>
            {
                {
                    typeof(IList),
                    (value, key) =>
                    {
                        var castValue = value as IList;

                        int intVal;
                        if (int.TryParse(key, out intVal))
                        {
                            return castValue != null && intVal < castValue.Count ? castValue[intVal] : null;
                        }

                        return null;
                    }
                },
                {
                    typeof(IDictionary<string, object>),
                    (value, key) =>
                    {
                        var castValue = value as IDictionary<string, object>;
                        return castValue != null && castValue.ContainsKey(key) ? castValue[key] : null;
                    }
                },
                {
                    typeof(IDictionary),
                    (value, key) =>
                    {
                        var castValue = value as IDictionary;
                        return castValue?[key];
                    }
                },
                {
                    typeof(object), GetValueFromObjectByName
                }
            };

            public static readonly IDictionary<string, Func<string, Tags, ParserOutput>> DefaultTokenGetters = new Dictionary
                <string, Func<string, Tags, ParserOutput>>
            {
                { "#", (s, tags) => new SectionToken(tags) { TokenType = s } },
                { "^", (s, tags) => new InvertedToken { TokenType = s } },
                { ">", (s, tags) => new PartialToken { TokenType = s } },
                { "&", (s, tags) => new UnescapedValueToken { TokenType = s } },
                { "name", (s, tags) => new EscapedValueToken { TokenType = s } },
                { "text", (s, tags) => new RawValueToken { TokenType = s } }
            };

            public static readonly IDictionary<Type, Func<object, IEnumerable>> DefaultEnumerationConverters = new Dictionary
                <Type, Func<object, IEnumerable>>();

            private static object GetValueFromObjectByName(object value, string key)
            {
                var type = value.GetType();
                var memberArr = type.GetMember(key, BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
                if (memberArr.Length != 1)
                {
                    return null;
                }

                var member = memberArr[0];
                if (member is FieldInfo)
                {
                    return ((FieldInfo)member).GetValue(value);
                }

                if (member is PropertyInfo)
                {
                    return ((PropertyInfo)member).GetValue(value, null);
                }

                if (member is MethodInfo)
                {
                    var methodMember = (MethodInfo)member;
                    return methodMember.GetParameters().Length == 0
                            ? methodMember.Invoke(value, null)
                            : null;
                }

                return null;
            }
        }
    }
}
