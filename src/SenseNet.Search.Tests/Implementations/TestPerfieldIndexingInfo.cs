﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SenseNet.Search.Tests.Implementations
{
    internal class TestPerfieldIndexingInfo_string : IPerFieldIndexingInfo
    {
        public string Analyzer { get; set; }
        public IFieldIndexHandler IndexFieldHandler { get; set; } = new TestIndexFieldHandler_string();
        public IndexingMode IndexingMode { get; set; } = IndexingMode.NotAnalyzed;
        public IndexStoringMode IndexStoringMode { get; set; } = IndexStoringMode.No;
        public IndexTermVector TermVectorStoringMode { get; set; } = IndexTermVector.No;
        public bool IsInIndex { get; } = true;
        public Type FieldDataType { get; set; } = typeof(string);
    }
    internal class TestPerfieldIndexingInfo_int: IPerFieldIndexingInfo
    {
        public string Analyzer { get; set; }
        public IFieldIndexHandler IndexFieldHandler { get; set; } = new TestIndexFieldHandler_int();
        public IndexingMode IndexingMode { get; set; } = IndexingMode.NotAnalyzed;
        public IndexStoringMode IndexStoringMode { get; set; } = IndexStoringMode.Yes;
        public IndexTermVector TermVectorStoringMode { get; set; } = IndexTermVector.No;
        public bool IsInIndex { get; } = true;
        public Type FieldDataType { get; set; } = typeof(int);
    }
}