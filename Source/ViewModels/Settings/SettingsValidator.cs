using System.ComponentModel.DataAnnotations;
using System.IO;
using Caliburn.Micro;
using FastBuild.Dashboard.Services.Build.SourceEditor;

namespace FastBuild.Dashboard.ViewModels.Settings
{
	public class SettingsValidator
	{
		public static ValidationResult ValidateBrokeragePath(string brokeragePath, ValidationContext context)
		{
			if (!Directory.Exists(brokeragePath))
			{
				return new ValidationResult("Brokerage path doesn't exist", new[] { nameof(SettingsViewModel.BrokeragePath) });
			}

			return ValidationResult.Success;
		}

		public static ValidationResult ValidateExternalSourceEditorPath(string editorPath, ValidationContext context)
		{
			if (!string.IsNullOrEmpty(editorPath))
			{
				if (!File.Exists(editorPath))
				{
					return new ValidationResult("Specified editor path does not exist",
						new[] {nameof(SettingsViewModel.ExternalSourceEditorPath)});
				}
			}
			else
			{
				if (!IoC.Get<IExternalSourceEditorService>().IsSelectedEditorAvailable)
				{
					return new ValidationResult("The editor cannot be found in Program Files, please locate it here",
						new[] { nameof(SettingsViewModel.ExternalSourceEditorPath) });
				}
			}

			return ValidationResult.Success;
		}

	}
}
