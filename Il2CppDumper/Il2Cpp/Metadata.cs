﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Il2CppDumper
{
    public sealed class Metadata : BinaryStream
    {
        public Il2CppGlobalMetadataHeader header;
        public Il2CppImageDefinition[] imageDefs;
        public Il2CppTypeDefinition[] typeDefs;
        public Il2CppMethodDefinition[] methodDefs;
        public Il2CppParameterDefinition[] parameterDefs;
        public Il2CppFieldDefinition[] fieldDefs;
        private Dictionary<int, Il2CppFieldDefaultValue> fieldDefaultValuesDic;
        private Dictionary<int, Il2CppParameterDefaultValue> parameterDefaultValuesDic;
        public Il2CppPropertyDefinition[] propertyDefs;
        public Il2CppCustomAttributeTypeRange[] attributeTypeRanges;
        private Dictionary<Il2CppImageDefinition, Dictionary<uint, int>> attributeTypeRangesDic;
        private Il2CppStringLiteral[] stringLiterals;
        private Il2CppMetadataUsageList[] metadataUsageLists;
        private Il2CppMetadataUsagePair[] metadataUsagePairs;
        public int[] attributeTypes;
        public int[] interfaceIndices;
        public Dictionary<uint, SortedDictionary<uint, uint>> metadataUsageDic;
        public long maxMetadataUsages;
        public int[] nestedTypeIndices;
        public Il2CppEventDefinition[] eventDefs;
        public Il2CppGenericContainer[] genericContainers;
        public Il2CppFieldRef[] fieldRefs;
        public Il2CppGenericParameter[] genericParameters;
        public int[] constraintIndices;
        public uint[] vtableMethods;
        public Il2CppRGCTXDefinition[] rgctxEntries;

        private Dictionary<uint, string> stringCache = new Dictionary<uint, string>();
        public ulong Address;

        private byte[] stringDecryptionBlob = null;
        private Dictionary<string, string> nameTranslation = new Dictionary<string, string>();
        private Regex nameTranslationMemberRegex = new Regex(@".+\/<(.+)>", RegexOptions.Compiled); // avoid a bunch of allocations

        public Metadata(Stream stream, MetadataDecryption.StringDecryptionData decData, string nameTranslationPath) : base(stream)
        {
            /*var sanity = ReadUInt32();
            if (sanity != 0xFAB11BAF)
            {
                throw new InvalidDataException("ERROR: Metadata file supplied is not valid metadata file.");
            }
            var version = ReadInt32();
            if (version < 16 || version > 27)
            {
                throw new NotSupportedException($"ERROR: Metadata file supplied is not a supported version[{version}].");
            }
            Version = version;*/
            Version = 24;
            header = ReadClass<Il2CppGlobalMetadataHeader>(0);

            //stringDecryptionBlob = File.ReadAllBytes("D:\\genshinimpactre\\1.5-dev\\decryption_blob.bin");
            stringDecryptionBlob = decData.stringDecryptionBlob;
            header.stringCount ^= (int)decData.stringCountXor;
            header.stringOffset ^= decData.stringOffsetXor;
            header.stringLiteralOffset ^= decData.stringLiteralOffsetXor;
            header.stringLiteralDataCount ^= (int)decData.stringLiteralDataCountXor;
            header.stringLiteralDataOffset ^= decData.stringLiteralDataOffsetXor;

            if (nameTranslationPath != null)
            {
                var nameTranslationFile = File.ReadAllLines(nameTranslationPath);
                foreach (var line in nameTranslationFile)
                {
                    if (line.StartsWith("#"))
                        continue;
                    var split = line.Split('⇨');
                    if (split.Length != 2)
                        throw new NotSupportedException($"unexpected split.Length {split.Length}");
                    //Console.WriteLine("{0} {1}", split[0], split[1]);
                    nameTranslation.Add(split[0], split[1]);
                }
                Console.WriteLine($"Loaded {nameTranslation.Count} lookup values");
            }

            /*if (version == 24)
            {
                if (header.stringLiteralOffset == 264)
                {
                    Version = 24.2f;
                    header = ReadClass<Il2CppGlobalMetadataHeader>(0);
                }
                else
                {
                    imageDefs = ReadMetadataClassArray<Il2CppImageDefinition>(header.imagesOffset, header.imagesCount);
                    if (imageDefs.Any(x => x.token != 1))
                    {
                        Version = 24.1f;
                    }
                }
            }*/
            imageDefs = ReadMetadataClassArray<Il2CppImageDefinition>(header.imagesOffset, header.imagesCount);
            typeDefs = ReadMetadataClassArray<Il2CppTypeDefinition>(header.typeDefinitionsOffset, header.typeDefinitionsCount);
            methodDefs = ReadMetadataClassArray<Il2CppMethodDefinition>(header.methodsOffset, header.methodsCount);
            parameterDefs = ReadMetadataClassArray<Il2CppParameterDefinition>(header.parametersOffset, header.parametersCount);
            fieldDefs = ReadMetadataClassArray<Il2CppFieldDefinition>(header.fieldsOffset, header.fieldsCount);
            var fieldDefaultValues = ReadMetadataClassArray<Il2CppFieldDefaultValue>(header.fieldDefaultValuesOffset, header.fieldDefaultValuesCount);
            var parameterDefaultValues = ReadMetadataClassArray<Il2CppParameterDefaultValue>(header.parameterDefaultValuesOffset, header.parameterDefaultValuesCount);
            fieldDefaultValuesDic = fieldDefaultValues.ToDictionary(x => x.fieldIndex);
            parameterDefaultValuesDic = parameterDefaultValues.ToDictionary(x => x.parameterIndex);
            propertyDefs = ReadMetadataClassArray<Il2CppPropertyDefinition>(header.propertiesOffset, header.propertiesCount);
            interfaceIndices = ReadClassArray<int>(header.interfacesOffset, header.interfacesCount / 4);
            nestedTypeIndices = ReadClassArray<int>(header.nestedTypesOffset, header.nestedTypesCount / 4);
            eventDefs = ReadMetadataClassArray<Il2CppEventDefinition>(header.eventsOffset, header.eventsCount);
            genericContainers = ReadMetadataClassArray<Il2CppGenericContainer>(header.genericContainersOffset, header.genericContainersCount);
            genericParameters = ReadMetadataClassArray<Il2CppGenericParameter>(header.genericParametersOffset, header.genericParametersCount);
            constraintIndices = ReadClassArray<int>(header.genericParameterConstraintsOffset, header.genericParameterConstraintsCount / 4);
            vtableMethods = ReadClassArray<uint>(header.vtableMethodsOffset, header.vtableMethodsCount / 4);
            stringLiterals = ReadMetadataClassArray<Il2CppStringLiteral>(header.stringLiteralOffset, header.stringLiteralCount);
            if (Version > 16 && Version < 27) //TODO
            {
                metadataUsageLists = ReadMetadataClassArray<Il2CppMetadataUsageList>(header.metadataUsageListsOffset, header.metadataUsageListsCount);
                metadataUsagePairs = ReadMetadataClassArray<Il2CppMetadataUsagePair>(header.metadataUsagePairsOffset, header.metadataUsagePairsCount);

                ProcessingMetadataUsage();

                fieldRefs = ReadMetadataClassArray<Il2CppFieldRef>(header.fieldRefsOffset, header.fieldRefsCount);
            }
            if (Version > 20)
            {
                attributeTypeRanges = ReadMetadataClassArray<Il2CppCustomAttributeTypeRange>(header.attributesInfoOffset, header.attributesInfoCount);
                attributeTypes = ReadClassArray<int>(header.attributeTypesOffset, header.attributeTypesCount / 4);
            }
            /*if (Version > 24)
            {
                attributeTypeRangesDic = new Dictionary<Il2CppImageDefinition, Dictionary<uint, int>>();
                foreach (var imageDef in imageDefs)
                {
                    var dic = new Dictionary<uint, int>();
                    attributeTypeRangesDic[imageDef] = dic;
                    var end = imageDef.customAttributeStart + imageDef.customAttributeCount;
                    for (int i = imageDef.customAttributeStart; i < end; i++)
                    {
                        dic.Add(attributeTypeRanges[i].token, i);
                    }
                }
            }*/
            if (Version <= 24.1f)
            {
                rgctxEntries = ReadMetadataClassArray<Il2CppRGCTXDefinition>(header.rgctxEntriesOffset, header.rgctxEntriesCount);
            }
        }

        private T[] ReadMetadataClassArray<T>(uint addr, int count) where T : new()
        {
            return ReadClassArray<T>(addr, count / SizeOf(typeof(T)));
        }

        public bool GetFieldDefaultValueFromIndex(int index, out Il2CppFieldDefaultValue value)
        {
            return fieldDefaultValuesDic.TryGetValue(index, out value);
        }

        public bool GetParameterDefaultValueFromIndex(int index, out Il2CppParameterDefaultValue value)
        {
            return parameterDefaultValuesDic.TryGetValue(index, out value);
        }

        public uint GetDefaultValueFromIndex(int index)
        {
            return (uint)(header.fieldAndParameterDefaultValueDataOffset + index);
        }

        private Dictionary<uint, bool> indexlist = new Dictionary<uint, bool>();
        public string GetStringFromIndex(uint index)
        {
            if (!stringCache.TryGetValue(index, out var result))
            {
                result = LookupNameTranslation(ReadStringToNull(header.stringOffset + index));
                stringCache.Add(index, result);
            }
            return result;
        }

        private void writetofile(string filename, string data)
        {
            StreamWriter file = new StreamWriter(@"G:\" + filename, true);
            file.WriteLine(data);
            file.Close();
        }

        public int GetCustomAttributeIndex(Il2CppImageDefinition imageDef, int customAttributeIndex, uint token)
        {
            if (Version > 24)
            {
                if (attributeTypeRangesDic[imageDef].TryGetValue(token, out var index))
                {
                    return index;
                }
                else
                {
                    return -1;
                }
            }
            else
            {
                return customAttributeIndex;
            }
        }

        public string GetStringLiteralFromIndex(uint index)
        {
            var stringLiteral = stringLiterals[index];
            Position = (ulong)(SizeOf(typeof(Il2CppGlobalMetadataHeader)) + stringLiteral.dataIndex);
            //Position = (ulong)(0x158 + stringLiteral.dataIndex);

            var buffer = ReadBytes((int)stringLiteral.length);
            for (var i = 0; i < stringLiteral.length; i++)
            {
                byte cl = (byte)(buffer[i] ^ stringDecryptionBlob[(0x1400 + i) % 0x5000]);
                byte al = (byte)(stringDecryptionBlob[i % 0x2800 + index % 0x2800] + i);
                buffer[i] = (byte)(cl ^ al);
            }

            return Encoding.UTF8.GetString(buffer);
        }

        private void ProcessingMetadataUsage()
        {
            metadataUsageDic = new Dictionary<uint, SortedDictionary<uint, uint>>();
            for (uint i = 1; i <= 6u; i++)
            {
                metadataUsageDic[i] = new SortedDictionary<uint, uint>();
            }
            foreach (var metadataUsageList in metadataUsageLists)
            {
                for (int i = 0; i < metadataUsageList.count; i++)
                {
                    var offset = metadataUsageList.start + i;
                    var metadataUsagePair = metadataUsagePairs[offset];
                    var usage = GetEncodedIndexType(metadataUsagePair.encodedSourceIndex);
                    var decodedIndex = GetDecodedMethodIndex(metadataUsagePair.encodedSourceIndex);
                    metadataUsageDic[usage][metadataUsagePair.destinationIndex] = decodedIndex;
                }
            }
            maxMetadataUsages = metadataUsageDic.Max(x => x.Value.Max(y => y.Key)) + 1;
        }

        public uint GetEncodedIndexType(uint index)
        {
            return (index & 0xE0000000) >> 29;
        }

        public uint GetDecodedMethodIndex(uint index)
        {
            if (Version >= 27)
            {
                return (index & 0x1FFFFFFEU) >> 1;
            }
            return index & 0x1FFFFFFFU;
        }

        public int SizeOf(Type type)
        {
            var size = 0;
            foreach (var i in type.GetFields())
            {
                var attr = (VersionAttribute)Attribute.GetCustomAttribute(i, typeof(VersionAttribute));
                if (attr != null)
                {
                    if (Version < attr.Min || Version > attr.Max)
                        continue;
                }
                var fieldType = i.FieldType;
                if (fieldType.IsPrimitive)
                {
                    size += GetPrimitiveTypeSize(fieldType.Name);
                }
                else if (fieldType.IsEnum)
                {
                    var e = fieldType.GetField("value__").FieldType;
                    size += GetPrimitiveTypeSize(e.Name);
                }
                else
                {
                    size += SizeOf(fieldType);
                }
            }
            return size;

            int GetPrimitiveTypeSize(string name)
            {
                switch (name)
                {
                    case "Int32":
                    case "UInt32":
                        return 4;
                    case "Int16":
                    case "UInt16":
                        return 2;
                    default:
                        return 0;
                }
            }
        }

        public string ReadString(int numChars)
        {
            var start = Position;
            // UTF8 takes up to 4 bytes per character
            var str = Encoding.UTF8.GetString(ReadBytes(numChars * 4)).Substring(0, numChars);
            // make our position what it would have been if we'd known the exact number of bytes needed.
            Position = start;
            ReadBytes(Encoding.UTF8.GetByteCount(str));
            return str;
        }

        public string LookupNameTranslation(string obfuscated)
        {
            string original;
            if (nameTranslation.TryGetValue(obfuscated, out original))
            {
                // TODO: not exactly accurate
                // unfortunately i can't use [^1] on this version of c#
                Match m = nameTranslationMemberRegex.Match(original);
                if (m.Success)
                {
                    var split = m.Groups[1].Value.Split('/');
                    return split[split.Length - 1];
                }
                var split2 = original.Split('/');
                return split2[split2.Length - 1];
            }
            return obfuscated;
        }
    }
}
