﻿using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using SenseNet.ContentRepository.Storage.Data;
using SenseNet.ContentRepository.Storage.Schema;
using SenseNet.Diagnostics;

namespace SenseNet.Storage.DataModel.Usage
{
    public class DatabaseUsageProfile
    {
        public Dimensions Content { get; private set; }
        public Dimensions OldVersions { get; private set; }
        public Dimensions Preview { get; private set; }
        public Dimensions System { get; private set; }
        public long OrphanedBlobs { get; private set; }
        public DateTime Executed { get; private set; }
        public TimeSpan ExecutionTime { get; private set; }

        private const int PreviewImage = 0;
        private const int SystemLast = 1;
        private const int ContentLast = 2;
        private const int SystemOld = 3;
        private const int ContentOld = 4;
        private int _previewImageTypeId;
        private (List<int> VersionIds, List<int> FileIds, Dimensions Dimensions)[] _db;

        private readonly DataProvider _dataProvider;

        public DatabaseUsageProfile(DataProvider dataProvider)
        {
            _dataProvider = dataProvider;
        }

        private bool ProcessNode(NodeModel node)
        {
            var category = -1;
            if (node.NodeTypeId == _previewImageTypeId)
            {
                category = PreviewImage;
            }
            else
            {
                var isSystem = node.OwnerId == 1;
                var isLast = node.IsLastDraft || node.IsLastPublic;

                category = isSystem
                    ? isLast ? SystemLast : SystemOld
                    : isLast ? ContentLast : ContentOld;
            }

            _db[category].VersionIds.Add(node.VersionId);
            var dimensions = _db[category].Dimensions;
            dimensions.Count++;
            dimensions.Metadata += node.DynamicPropertiesSize + node.ContentListPropertiesSize + node.ChangedDataSize;
            dimensions.Index += node.IndexSize;

            return false;
        }
        private bool ProcessLongText(LongTextModel model)
        {
            for (int i = 0; i < _db.Length; i++)
            {
                if (_db[i].VersionIds.Contains(model.VersionId))
                {
                    _db[i].Dimensions.Text += model.Size;
                    break;
                }
            }
            return false;
        }
        private bool ProcessBinary(BinaryPropertyModel model)
        {
            for (int i = 0; i < _db.Length; i++)
            {
                if (_db[i].VersionIds.Contains(model.VersionId))
                {
                    _db[i].FileIds.Add(model.FileId);
                    break;
                }
            }
            return false;
        }
        private bool ProcessFile(FileModel model)
        {
            var found = false;
            var size = Math.Max(model.Size, model.StreamSize);
            for (int i = 0; i < _db.Length; i++)
            {
                if (_db[i].FileIds.Contains(model.FileId))
                {
                    _db[i].Dimensions.Blob += size;
                    found = true;
                    break;
                }
            }

            if (!found)
                OrphanedBlobs += size;

            return false;
        }

        public Task BuildProfileAsync(CancellationToken cancel = default)
        {
            using (var op = SnTrace.System.StartOperation("Load DatabaseUsageProfile"))
            {
                var start = DateTime.UtcNow;

                _previewImageTypeId = NodeType.GetByName("PreviewImage").Id;

                _db = new (List<int> VersionIds, List<int> FileIds, Dimensions Dimensions)[5];
                for (int i = 0; i < _db.Length; i++)
                    _db[i] = (new List<int>(), new List<int>(), new Dimensions());

                _dataProvider.LoadDatabaseUsageProfile(ProcessNode, ProcessLongText, ProcessBinary, ProcessFile);

                this.Preview = _db[PreviewImage].Dimensions.Clone();
                this.System = _db[SystemLast].Dimensions.Clone();
                this.Content = _db[ContentLast].Dimensions.Clone();
                this.OldVersions = Dimensions.Sum(_db[SystemOld].Dimensions, _db[ContentOld].Dimensions);

                var now = DateTime.UtcNow;
                this.Executed = now;
                this.ExecutionTime = now - start;

                op.Successful = true;
                return Task.CompletedTask;
            }
        }
    }
}