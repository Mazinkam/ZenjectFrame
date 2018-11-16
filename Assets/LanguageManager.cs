using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Zenject;

namespace  Frame.Settings
{
	public interface ILanguageManager
	{
		string CurrentLanguage { get; set; }
		
	}

	public class LanguageManager : ILanguageManager, IInitializable 
	{
		public string CurrentLanguage { get; set; }
		public void Initialize()
		{
			Debug.Log("LanguageManager initalized, current Language " + CurrentLanguage);
		}
	}
}
