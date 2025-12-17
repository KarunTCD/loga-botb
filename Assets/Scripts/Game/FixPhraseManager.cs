using UnityEngine;
using System;
using System.Linq;

public class FixPhraseManager : MonoBehaviour
{
    private string[] wordList;

    void Start()
    {
        LoadWordList();
    }
    private void LoadWordList()
    {
        TextAsset wordListJSON = Resources.Load<TextAsset>("wordlist");
        if (wordListJSON == null)
        {
            Debug.LogError("wordlist.json not found in Resources folder");
            return;
        }

        // Simple string split approach
        string content = wordListJSON.text.Trim();
        // Remove brackets at start and end
        content = content.Substring(1, content.Length - 2);
        // Split by commas and clean up quotes
        wordList = content.Split(',')
                         .Select(w => w.Trim().Trim('"'))
                         .ToArray();
    }

    public string EncodeLocation(double latitude, double longitude)
    {
        latitude = Math.Round(latitude, 4);
        longitude = Math.Round(longitude, 4);

        double adjustedLat = latitude + 90;
        double adjustedLon = longitude + 180;

        string latString = (adjustedLat * 10000).ToString("0000000");
        string lonString = (adjustedLon * 10000).ToString("0000000");

        int firstIndex = int.Parse(latString.Substring(0, 4));
        int secondIndex = int.Parse(lonString.Substring(0, 4)) + 2000;
        int thirdIndex = int.Parse($"{latString[4]}{latString[5]}{lonString[4]}") + 5610;
        int fourthIndex = int.Parse($"{latString[6]}{lonString[5]}{lonString[6]}") + 6610;

        return $"{wordList[firstIndex]} {wordList[secondIndex]} {wordList[thirdIndex]} {wordList[fourthIndex]}";
    }

    public (double latitude, double longitude) DecodePhrase(string[] words)
    {
        if (words.Length < 2) return (0, 0);

        int[] indices = new int[4];
        for (int i = 0; i < words.Length; i++)
        {
            indices[i] = Array.IndexOf(wordList, words[i].ToLower());
        }

        // First two words give rough location
        double latitude = (indices[0] / 10.0) - 90;
        double longitude = ((indices[1] - 2000) / 10.0) - 180;

        if (words.Length >= 3)
        {
            // Third word refines location
            int thirdIndex = indices[2] - 5610;
            string thirdPadded = thirdIndex.ToString("000");
            latitude += double.Parse($"0.0{thirdPadded[0]}{thirdPadded[1]}");
            longitude += double.Parse($"0.0{thirdPadded[2]}");
        }

        if (words.Length == 4)
        {
            // Fourth word gives final precision
            int fourthIndex = indices[3] - 6610;
            string fourthPadded = fourthIndex.ToString("000");
            latitude += double.Parse($"0.000{fourthPadded[0]}");
            longitude += double.Parse($"0.00{fourthPadded[1]}{fourthPadded[2]}");
        }

        return (latitude, longitude);
    }
}