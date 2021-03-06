﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using WFFM.ConversionTool.Library.Logging;
using WFFM.ConversionTool.Library.Models.Metadata;
using WFFM.ConversionTool.Library.Repositories;

namespace WFFM.ConversionTool.Library.Converters.FieldValueConverters
{
	public class FileUploadConverter : BaseFieldConverter
	{
		private ISourceMasterRepository _sourceMasterRepository;
		private AppSettings _appSettings;
		private ILogger _logger;

		public FileUploadConverter(ISourceMasterRepository sourceMasterRepository, AppSettings appSettings, ILogger logger)
		{
			_sourceMasterRepository = sourceMasterRepository;
			_appSettings = appSettings;
			_logger = logger;
		}

		public override string ConvertValue(string sourceValue)
		{
			// Parse the value to get the media item ID
			// Example: sitecore://master/{A1207618-AFC1-465A-A45A-5F1C47A59B34}?lang=en&ver=1
			var mediaItemRegexMatch = Regex.Match(sourceValue, @"sitecore:\/\/master\/(.*)\?lang=(.*)&ver=1");
			var mediaItemId = mediaItemRegexMatch.Groups[1].Value;
			var mediaItemLanguage = mediaItemRegexMatch.Groups[2].Value;

			if (string.IsNullOrEmpty(mediaItemId) || !Guid.TryParse(mediaItemId, out var mediaItemGuid))
			{
				return sourceValue;
			}

			// Get the media item from source master db and map fields
			var mediaItem = _sourceMasterRepository.GetSitecoreItem(mediaItemGuid);

			if (string.IsNullOrEmpty(mediaItem.Name)) return sourceValue;

			return mediaItemGuid.ToString("D");
		}
	}
}
