﻿using System;
using System.Collections;
using System.Globalization;
using System.Text;
using System.Xml.XPath;
using System.Xml.Xsl;

namespace Meziantou.Framework.Html.Tests
{
    internal abstract class HtmlXsltFunction : IXsltContextFunction
    {
        protected HtmlXsltFunction(HtmlXsltContext context, string prefix, string name, XPathResultType[] argTypes)
        {
            Context = context;
            Prefix = prefix;
            Name = name;
            ArgTypes = argTypes;
        }

        public HtmlXsltContext Context { get; }
        public string Prefix { get; }
        public string Name { get; }
        public XPathResultType[] ArgTypes { get; }

        public static object CreateXsltArgument(HtmlXsltContext context) => new XsltArgument(context);

        public abstract object Invoke(XsltContext xsltContext, object[] args, XPathNavigator docContext);

        public virtual int Maxargs => Minargs;

        public virtual int Minargs => 1;

        public virtual XPathResultType ReturnType => XPathResultType.String;

        public static T ConvertTo<T>(object argument, CultureInfo ci, T defaultValue)
        {
            if (argument == null)
                return defaultValue;

            if (argument is T)
                return (T)argument;

            if (argument is XPathNodeIterator it)
            {
                if (!it.MoveNext())
                    return defaultValue;

                if (it.Current is HtmlNodeNavigator n && n.CurrentNode is T result)
                    return result;

                return defaultValue;
            }

            if (argument is IEnumerable enumerable && (!(argument is string)))
            {
                foreach (var arg in enumerable)
                {
                    if (arg is T result)
                        return result;

                    break;
                }
            }

            return defaultValue;
        }

        public static string ConvertToString(object argument, bool outer, string separator)
        {
            if (argument == null)
                return null;

            if (argument is string s)
                return s;

            if (argument is XPathNodeIterator it)
            {
                if (!it.MoveNext())
                    return null;

                var sb = new StringBuilder();
                do
                {
                    if (it.Current is HtmlNodeNavigator n && n.CurrentNode != null)
                    {
                        if (sb.Length > 0 && separator != null)
                        {
                            sb.Append(separator);
                        }

                        if (n.CurrentNode is HtmlElement element)
                        {
                            var clone = (HtmlElement)element.Clone();
                            sb.Append(outer ? clone.OuterHtml : clone.InnerHtml);
                        }
                        else
                        {
                            sb.Append(n.CurrentNode.Value);
                        }
                    }
                }
                while (it.MoveNext());
                return sb.ToString();
            }

            if (argument is IEnumerable enumerable)
            {
                StringBuilder sb = null;
                foreach (var arg in enumerable)
                {
                    if (sb == null)
                    {
                        sb = new StringBuilder();
                    }

                    if (sb.Length > 0 && separator != null)
                    {
                        sb.Append(separator);
                    }

                    var s2 = ConvertToString(arg, outer, separator);
                    if (s2 != null)
                    {
                        sb.Append(s2);
                    }
                }

                return sb?.ToString();
            }

            return string.Format(CultureInfo.InvariantCulture, "{0}", argument);
        }

        internal static bool IsNull(object arg)
        {
            if (arg == null || Convert.IsDBNull(arg))
                return true;

            if (arg is string)
                return false;

            if (arg is XPathNodeIterator it)
            {
                it = it.Clone();
                if (!it.MoveNext())
                    return true;

                object current = it.Current;
                return IsNull(current);
            }

            if (arg is IEnumerable e)
            {
                foreach (var o in e)
                    return false;

                return true;
            }
            return false;
        }

        private class XsltArgument
        {
            public XsltArgument(HtmlXsltContext context)
            {
                Context = context;
            }

            public HtmlXsltContext Context { get; }

            public string Lowercase(object obj)
            {
                return (string)new Lowercase(Context, "Lowercase").Invoke(null, new[] { obj }, null);
            }

            // add methods as needed
        }

        public static IXsltContextFunction GetBuiltIn(HtmlXsltContext context, string prefix, string name, XPathResultType[] argTypes)
        {
            if (name == "lowercase")
                return new Lowercase(context, name);

            return null;
        }

        public class Lowercase : HtmlXsltFunction
        {
            public Lowercase(HtmlXsltContext context, string name)
                : base(context, null, name, null)
            {
            }

            public override object Invoke(XsltContext xsltContext, object[] args, XPathNavigator docContext)
            {
                return ConvertToString(args, false, null)?.ToLower();
            }
        }
    }
}