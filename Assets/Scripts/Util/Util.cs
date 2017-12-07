// General utility functions that are useful throughout the app.

using System;
using UnityEngine;

public static class Util {
    // TODO: should include comma or not? Sometimes that makes it too vertical.
    public static string[] punctuation = {";", ".", "?", "\"", "!", ","};

    public static string FileNameToStoryName(string fileName) {
        return fileName.Substring(0,
            fileName.LastIndexOf("_", StringComparison.CurrentCulture)
        );
    }

    // Returns true if the given word should be the last word of a stanza,
    // such as if that word ends a phrase or sentence.
    public static bool WordShouldEndStanza(string word) {
        foreach (string p in punctuation) {
            if (word.EndsWith(p, StringComparison.CurrentCulture)) {
                return true;
            }
        }
        return false;
    }

    // Gets the sprite for a particular story image file.
    public static Sprite GetStorySprite(string imageFile) {
        string storyName = Util.FileNameToStoryName(imageFile);
        string fullImagePath = "StoryPages/" + storyName + "/" + imageFile;
        return Resources.Load<Sprite>(fullImagePath);
    }


    // Turns something like "the_hungry_toad" into "The Hungry Toad"
    public static string HumanReadableStoryName(string story) {
        string[] words = story.Split('_');
        string humanReadable = "";
        foreach (string word in words) {
            if (word.Length > 1) {
                humanReadable += char.ToUpper(word[0]) + word.Substring(1);
            } else {
                humanReadable += word.ToUpper();
            }
            humanReadable += " ";
        }
        return humanReadable.TrimEnd(' ');
    }

    // Get the title page sprite for a story name.
    public static Sprite GetTitleSprite(string story) {
        return Util.GetStorySprite(story + "_01");
    }

    // Return true if the two positions (rectangles) overlap enough that we
    // think they refer to the same object. Based on a heuristic, not exact.
    public static bool RefersToSameObject(Position first, Position second) {
        // Check if the area of the rectangle of overlap is larger than 50%
        // of the area of the smaller input rectangle.
        Position leftMost = first;
        Position rightMost = second;
        if (first.left > second.left) {
            leftMost = second;
            rightMost = first;
        }
        float xOverlap = Math.Max(0, (leftMost.left + leftMost.width) - rightMost.left);
        if (rightMost.left + rightMost.width < leftMost.left + rightMost.width) {
            // Special case for complete overlap (rightMost is contained in leftMost).
            xOverlap = rightMost.width;
        }
        Position topMost = first;
        Position bottomMost = second;
        if (first.top < second.top) {
            topMost = second;
            bottomMost = first;
        }
        float yOverlap = Math.Max(0, bottomMost.top - (topMost.top - topMost.height));
        if (bottomMost.top - bottomMost.height > topMost.top - topMost.height) {
            // Complete overlap.
            yOverlap = bottomMost.height;
        }
        float overlapArea = xOverlap * yOverlap;
        float minArea = Math.Min(first.width * first.height, second.width * second.height);
        return overlapArea / minArea > 0.5;
    }

    // Returns absolute screen width (meaning width is the larger of the two
    // values, not necessarily the horizonal one).
    public static int GetScreenWidth() {
        return Math.Max(Screen.width, Screen.height);
    }

    public static int GetScreenHeight() {
        return Math.Min(Screen.width, Screen.height);
    }

    public static void SetSize(GameObject panel, Vector2 newSize) {
        panel.GetComponent<RectTransform>().sizeDelta = newSize;
    }
}
