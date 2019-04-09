﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using Newtonsoft.Json;
using WFFM.ConversionTool.Library.Factories;
using WFFM.ConversionTool.Library.Models;
using WFFM.ConversionTool.Library.Models.Metadata;
using WFFM.ConversionTool.Library.Models.Sitecore;
using WFFM.ConversionTool.Library.Providers;

namespace WFFM.ConversionTool.Library.Converters
{
	public class ItemConverter : IItemConverter
	{
		private IFieldFactory _fieldFactory;
		private MetadataTemplate _itemMetadataTemplate;
		private AppSettings _appSettings;
		private IMetadataProvider _metadataProvider;
		private IItemFactory _itemFactory;

		private readonly string _baseFieldConverterType = "WFFM.ConversionTool.Library.Converters.BaseFieldConverter, WFFM.ConversionTool.Library";

		public ItemConverter(IFieldFactory fieldFactory, AppSettings appSettings, IMetadataProvider metadataProvider, IItemFactory itemFactory)
		{
			_fieldFactory = fieldFactory;
			_appSettings = appSettings;
			_metadataProvider = metadataProvider;
			_itemFactory = itemFactory;
		}

		public List<SCItem> Convert(SCItem scItem, Guid destParentId)
		{
			_itemMetadataTemplate = _metadataProvider.GetItemMetadataByTemplateId(scItem.TemplateID);
			if (_itemMetadataTemplate.sourceMappingFieldId != null && _itemMetadataTemplate.sourceMappingFieldId != Guid.Empty)
			{
				var mappedMetadataTemplate = _metadataProvider.GetItemMetadataBySourceMappingFieldValue(scItem.Fields
					.FirstOrDefault(f => f.FieldId == _itemMetadataTemplate.sourceMappingFieldId)?.Value);
				if (mappedMetadataTemplate != null)
				{
					_itemMetadataTemplate = mappedMetadataTemplate;
				}
			}

			List<SCItem> destItems = ConvertItemAndFields(scItem, destParentId);
			return destItems;
		}

		private List<SCItem> ConvertItemAndFields(SCItem sourceItem, Guid destParentId)
		{
			List<SCItem> destItems = new List<SCItem>();
			var destItem = new SCItem()
			{
				ID = sourceItem.ID,
				Name = sourceItem.Name,
				MasterID = Guid.Empty,
				ParentID = destParentId,
				Created = sourceItem.Created,
				Updated = sourceItem.Updated,
				TemplateID = _itemMetadataTemplate.destTemplateId,
				Fields = ConvertFields(sourceItem.Fields)
			};
			destItems.Add(destItem);
			return destItems;
		}

		private List<SCField> ConvertFields(List<SCField> fields)
		{
			var destFields = new List<SCField>();

			var itemId = fields.First().ItemId;

			IEnumerable<Tuple<string, int>> langVersions = fields.Where(f => f.Version != null && f.Language != null).Select(f => new Tuple<string, int>(f.Language, (int)f.Version)).Distinct();
			var languages = fields.Where(f => f.Language != null).Select(f => f.Language).Distinct();

			// Migrate existing fields
			if (_itemMetadataTemplate.fields.existingFields != null)
			{
				var filteredExistingFields = fields.Where(f =>
					_itemMetadataTemplate.fields.existingFields.Select(mf => mf.fieldId).Contains(f.FieldId));

				foreach (var filteredExistingField in filteredExistingFields)
				{
					var existingField =
						_itemMetadataTemplate.fields.existingFields.FirstOrDefault(mf => mf.fieldId == filteredExistingField.FieldId);

					if (existingField != null)
					{
						destFields.Add(filteredExistingField);
					}
				}
			}

			// Convert fields
			if (_itemMetadataTemplate.fields.convertedFields != null)
			{
				// Select only fields that are mapped
				var filteredConvertedFields = fields.Where(f =>
					_itemMetadataTemplate.fields.convertedFields.Select(mf => mf.sourceFieldId).Contains(f.FieldId));

				foreach (var filteredConvertedField in filteredConvertedFields)
				{
					var convertedField =
						_itemMetadataTemplate.fields.convertedFields.FirstOrDefault(mf =>
							mf.sourceFieldId == filteredConvertedField.FieldId);

					if (convertedField != null)
					{
						// Process fields that have multiple dest fields
						if (convertedField.destFields != null && convertedField.destFields.Any())
						{
							var valueElements = GetXmlElementNames(filteredConvertedField.Value);
							var filteredValueElements =
								convertedField.destFields.Where(f => valueElements.Contains(f.sourceElementName.ToLower()) && f.destFieldId != null);

							foreach (var valueXmlElementMapping in filteredValueElements)
							{
								IFieldConverter converter = InitConverter(valueXmlElementMapping.fieldConverter);

								SCField destField = converter?.ConvertValueElement(filteredConvertedField, (Guid)valueXmlElementMapping.destFieldId, GetXmlElementValue(filteredConvertedField.Value, valueXmlElementMapping.sourceElementName));

								if (destField != null && destField.FieldId != Guid.Empty)
								{
									destFields.Add(destField);
								}
							}
						}
						// Process fields that have a single dest field
						else if (convertedField.destFieldId != null)
						{
							IFieldConverter converter = InitConverter(convertedField.fieldConverter);
							SCField destField = converter?.ConvertField(filteredConvertedField, (Guid)convertedField.destFieldId);

							if (destField != null && destField.FieldId != Guid.Empty)
							{
								destFields.Add(destField);
							}
						}
					}
				}
			}

			// Create new fields
			foreach (var newField in _itemMetadataTemplate.fields.newFields)
			{
				destFields.AddRange(_fieldFactory.CreateFields(newField, itemId, langVersions, languages));
			}

			return destFields;
		}

		private IFieldConverter InitConverter(string converterName)
		{
			var converterType = _baseFieldConverterType;
			if (converterName != null)
			{
				var metaConverter = _appSettings.converters.FirstOrDefault(c => c.name == converterName)?.converterType;
				if (!string.IsNullOrEmpty(metaConverter))
				{
					converterType = metaConverter;
				}
			}
			return ConverterInstantiator.CreateInstance(converterType);
		}

		private List<string> GetXmlElementNames(string fieldValue)
		{
			List<string> elementNames = new List<string>();
			XmlDocument xmlDocument = new XmlDocument();
			xmlDocument.LoadXml(AddParentNodeAndEncodeElementValue(fieldValue));

			foreach (XmlNode childNode in xmlDocument.ChildNodes.Item(0).ChildNodes)
			{
				elementNames.Add(childNode.Name.ToLower());
			}

			return elementNames;
		}

		private string GetXmlElementValue(string fieldValue, string elementName)
		{
			if (!string.IsNullOrEmpty(fieldValue) && !string.IsNullOrEmpty(elementName))
			{
				XmlDocument xmlDocument = new XmlDocument();
				xmlDocument.LoadXml(AddParentNodeAndEncodeElementValue(fieldValue));

				XmlNodeList elementsByTagName = xmlDocument.GetElementsByTagName(elementName);

				if (elementsByTagName.Count > 0)
				{
					var element = elementsByTagName.Item(0);
					return element?.InnerXml;
				}
			}
			return string.Empty;
		}

		private string AddParentNodeAndEncodeElementValue(string fieldValue)
		{
			// Add parent xml element to value
			fieldValue = string.Format("<ParentNode>{0}</ParentNode>", fieldValue);
			// Escape special chars in text value
			fieldValue = fieldValue.Replace("&", "&amp;");

			return fieldValue;
		}
	}

}