﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using SenseNet.ContentRepository;
using SenseNet.ContentRepository.Schema;
using SenseNet.ContentRepository.Storage;
using SenseNet.ContentRepository.Storage.Events;
using SenseNet.ContentRepository.Versioning;
using SenseNet.Diagnostics;
using SenseNet.Events;

// ReSharper disable InconsistentNaming
namespace SenseNet.WebHooks
{
    /// <summary>
    /// A Content handler that represents a webhook subscription.
    /// </summary>
    [ContentHandler]
    public class WebHookSubscription : GenericContent
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="WebHookSubscription"/> class.
        /// </summary>
        /// <param name="parent">The parent.</param>
        public WebHookSubscription(Node parent) : this(parent, null) { }
        /// <summary>
        /// Initializes a new instance of the <see cref="WebHookSubscription"/> class.
        /// </summary>
        /// <param name="parent">The parent.</param>
        /// <param name="nodeTypeName">Name of the node type.</param>
        public WebHookSubscription(Node parent, string nodeTypeName) : base(parent, nodeTypeName) { }
        /// <summary>
        /// Initializes a new instance of the <see cref="WebHookSubscription"/> class during the loading process.
        /// Do not use this constructor directly in your code.
        /// </summary>
        protected WebHookSubscription(NodeToken nt) : base(nt) { }

        private const string EnabledPropertyName = "Enabled";
        [RepositoryProperty(EnabledPropertyName, RepositoryDataType.Int)]
        public bool Enabled
        {
            get => base.GetProperty<int>(EnabledPropertyName) != 0;
            set => base.SetProperty(EnabledPropertyName, value ? 1 : 0);
        }

        private const string UrlPropertyName = "WebHookUrl";
        [RepositoryProperty(UrlPropertyName, RepositoryDataType.String)]
        public string Url
        {
            get => base.GetProperty<string>(UrlPropertyName);
            set => base.SetProperty(UrlPropertyName, value);
        }

        private const string HttpMethodPropertyName = "WebHookHttpMethod";
        [RepositoryProperty(HttpMethodPropertyName, RepositoryDataType.String)]
        public string HttpMethod
        {
            get => base.GetProperty<string>(HttpMethodPropertyName);
            set => base.SetProperty(HttpMethodPropertyName, value);
        }

        private const string FilterPropertyName = "WebHookFilter";
        [RepositoryProperty(FilterPropertyName, RepositoryDataType.Text)]
        public string Filter
        {
            get => base.GetProperty<string>(FilterPropertyName);
            set => base.SetProperty(FilterPropertyName, value);
        }

        private const string HeadersPropertyName = "WebHookHeaders";
        [RepositoryProperty(HeadersPropertyName, RepositoryDataType.Text)]
        public string Headers
        {
            get => base.GetProperty<string>(HeadersPropertyName);
            set => base.SetProperty(HeadersPropertyName, value);
        }

        private const string HeadersCacheKey = "WebHookHeaders.Key";
        public IDictionary<string, string> HttpHeaders { get; private set; }

        private const string FilterCacheKey = "WebHookFilter.Key";
        public WebHookFilterData FilterData { get; set; }

        private const string FilterQueryCacheKey = "WebHookFilterQuery.Key";
        public string FilterQuery { get; set; }

        private const string InvalidFieldsPropertyName = "InvalidFields";
        private const string InvalidFieldsCacheKey = "InvalidFields.Key";
        public string InvalidFields { get; private set; }

        private const string IsValidPropertyName = "IsValid";
        public bool IsValid => string.IsNullOrEmpty(InvalidFields);

        private static WebHookEventType[] AllEventTypes { get; } = (WebHookEventType[])Enum.GetValues(typeof(WebHookEventType));

        public WebHookEventType[] GetRelevantEventTypes(ISnEvent snEvent)
        {
            // Check if the subscription contains the type of the content. Currently we
            // treat the defined content types as "exact" types, meaning you have to choose
            // the appropriate type, no type inheritance is taken into account.
            var node = snEvent.NodeEventArgs.SourceNode;
            var contentType = FilterData.ContentTypes.FirstOrDefault(ct => ct.Name == node.NodeType.Name);
            if (contentType == null)
                return Array.Empty<WebHookEventType>();

            var selectedEvents = FilterData.TriggersForAllEvents || contentType.Events.Contains(WebHookEventType.All)
                ? AllEventTypes
                : contentType.Events ?? Array.Empty<WebHookEventType>();

            if (!selectedEvents.Any())
                return Array.Empty<WebHookEventType>();

            //UNDONE: event types are internal, cannot cast sn event
            var eventTypeName = snEvent.GetType().Name;

            switch (eventTypeName)
            {
                // Create and Delete are strong events, they cannot be paired with other events (e.g. with Modify).
                case "NodeCreatedEvent":
                    if (selectedEvents.Contains(WebHookEventType.Create))
                        return new[] {WebHookEventType.Create};
                    break;
                case "NodeModifiedEvent":
                    return CollectVersioningEvents(selectedEvents, snEvent);
                case "NodeForcedDeletedEvent":
                    if (selectedEvents.Contains(WebHookEventType.Delete))
                        return new[] { WebHookEventType.Delete };
                    break;
            }

            return Array.Empty<WebHookEventType>();
        }

        private WebHookEventType[] CollectVersioningEvents(WebHookEventType[] selectedEvents, ISnEvent snEvent)
        {
            var relevantEvents = new List<WebHookEventType>();
            var gc = snEvent.NodeEventArgs.SourceNode as GenericContent;
            var approvingMode = gc?.ApprovingMode ?? ApprovingType.False;
            var versioningMode = gc?.VersioningMode ?? VersioningType.None;
            var eventArgs = snEvent.NodeEventArgs as NodeEventArgs;
            var previousVersion = GetPreviousVersion();
            var currentVersion = snEvent.NodeEventArgs.SourceNode.Version;

            foreach (var eventType in selectedEvents)
            {
                // check whether this event happened
                switch (eventType)
                {
                    case WebHookEventType.Modify:
                        relevantEvents.Add(WebHookEventType.Modify);
                        break;
                    case WebHookEventType.Approve:
                        // Hidden approve: when the admin or owner publishes a document directly
                        // from draft to approved.
                        if ((previousVersion?.Status == VersionStatus.Pending &&
                            currentVersion.Status == VersionStatus.Approved) ||
                            (approvingMode == ApprovingType.True &&
                             previousVersion?.Status == VersionStatus.Draft &&
                             currentVersion.Status == VersionStatus.Approved))
                        {
                            relevantEvents.Add(WebHookEventType.Approve);
                        }
                        break;
                    case WebHookEventType.Publish:
                        //UNDONE: true? publish is relevant only if approving is on
                        // Users want this event when...?
                        if ((approvingMode == ApprovingType.True &&
                             currentVersion.Status == VersionStatus.Pending) ||
                            (approvingMode == ApprovingType.False &&
                             previousVersion?.Status == VersionStatus.Draft &&
                             currentVersion.Status == VersionStatus.Approved))
                        {
                            relevantEvents.Add(WebHookEventType.Publish);
                        }
                        break;
                    case WebHookEventType.Reject:
                        if (approvingMode == ApprovingType.True &&
                            previousVersion?.Status == VersionStatus.Pending &&
                            currentVersion.Status == VersionStatus.Rejected)
                        {
                            relevantEvents.Add(WebHookEventType.Reject);
                        }
                        break;
                    case WebHookEventType.CheckIn:
                        if (previousVersion?.Status == VersionStatus.Locked &&
                            currentVersion.Status != VersionStatus.Locked)
                        {
                            relevantEvents.Add(WebHookEventType.CheckIn);
                        }
                        break;
                    case WebHookEventType.CheckOut:
                        if (previousVersion?.Status != VersionStatus.Locked &&
                            currentVersion.Status == VersionStatus.Locked)
                        {
                            relevantEvents.Add(WebHookEventType.CheckOut);
                        }
                        break;
                }
            }

            return relevantEvents.Distinct().ToArray();

            VersionNumber GetPreviousVersion()
            {
                var chv = eventArgs?.ChangedData?.FirstOrDefault(cd => cd.Name == "Version");
                if (chv == null)
                    return null;

                return VersionNumber.TryParse((string) chv.Original, out var oldVersion) ? oldVersion : null;
            }
        }

        // ===================================================================================== Overrides

        private static readonly Dictionary<string, bool> InvalidFieldNames = new Dictionary<string, bool>
        {
            { FilterPropertyName, false },
            { HeadersPropertyName, false }
        };

        protected override void OnLoaded(object sender, NodeEventArgs e)
        {
            base.OnLoaded(sender, e);

            var invalidFields = (Dictionary<string, bool>)GetCachedData(InvalidFieldsCacheKey)
                ?? new Dictionary<string, bool>(InvalidFieldNames);

            #region Headers
            HttpHeaders = (IDictionary<string, string>)GetCachedData(HeadersCacheKey);
            if (HttpHeaders == null)
            {
                try
                {
                    HttpHeaders = JsonConvert.DeserializeObject<Dictionary<string, string>>(Headers ?? string.Empty);
                    invalidFields[HeadersPropertyName] = false;
                }
                catch (Exception ex)
                {
                    invalidFields[HeadersPropertyName] = true;
                    SnLog.WriteWarning($"Error parsing webhook headers on subscription {Path}. {ex.Message}");
                }

                if (HttpHeaders == null)
                    HttpHeaders = new Dictionary<string, string>();

                SetCachedData(HeadersCacheKey, HttpHeaders);
            }
            #endregion

            #region Filter data
            FilterData = (WebHookFilterData)GetCachedData(FilterCacheKey);
            if (FilterData == null)
            {
                try
                {
                    FilterData = JsonConvert.DeserializeObject<WebHookFilterData>(Filter ?? string.Empty);
                    invalidFields[FilterPropertyName] = false;
                }
                catch (Exception ex)
                {
                    invalidFields[FilterPropertyName] = true;
                    SnLog.WriteWarning($"Error parsing webhook filters on subscription {Path}. {ex.Message}");
                }

                if (FilterData == null)
                    FilterData = new WebHookFilterData();

                SetCachedData(FilterCacheKey, FilterData);
            }
            #endregion

            #region Filter query

            FilterQuery = (string)GetCachedData(FilterQueryCacheKey);
            if (FilterQuery == null)
            {
                try
                {
                    // subtree filter
                    var queryBuilder = new StringBuilder($"+InTree:'{FilterData.Path ?? "/Root"}'");

                    // add exact type filters
                    if (FilterData?.ContentTypes?.Any() ?? false)
                    {
                        queryBuilder.Append($" +Type:({string.Join(" ", FilterData.ContentTypes.Select(ct => ct.Name))})");
                    }

                    FilterQuery = queryBuilder.ToString();
                }
                catch (Exception ex)
                {
                    invalidFields[FilterPropertyName] = true;
                    SnLog.WriteWarning($"Error building webhook filter query on subscription {Path}. {ex.Message}");
                }

                if (FilterQuery == null)
                    FilterQuery = string.Empty;

                SetCachedData(FilterQueryCacheKey, FilterQuery);
            }

            #endregion

            InvalidFields = string.Join(";", invalidFields.Where(kv => kv.Value).Select(kv => kv.Key));
            SetCachedData(InvalidFieldsCacheKey, invalidFields);
        }

        /// <inheritdoc />
        public override object GetProperty(string name)
        {
            switch (name)
            {
                case EnabledPropertyName: return this.Enabled;
                case IsValidPropertyName: return this.IsValid;
                case InvalidFieldsPropertyName: return this.InvalidFields;
                case UrlPropertyName: return this.Url;
                case HttpMethodPropertyName: return this.HttpMethod;
                case FilterPropertyName: return this.Filter;
                case HeadersPropertyName: return this.Headers;
                default: return base.GetProperty(name);
            }
        }

        /// <inheritdoc />
        public override void SetProperty(string name, object value)
        {
            switch (name)
            {
                case EnabledPropertyName:
                    this.Enabled = (bool)value;
                    break;
                case IsValidPropertyName:
                case InvalidFieldsPropertyName:
                    break;
                case UrlPropertyName:
                    this.Url = (string)value;
                    break;
                case HttpMethodPropertyName:
                    this.HttpMethod = (string)value;
                    break;
                case FilterPropertyName:
                    this.Filter = (string)value;
                    break;
                case HeadersPropertyName:
                    this.Headers = (string)value;
                    break;
                default:
                    base.SetProperty(name, value);
                    break;
            }
        }
    }
}
