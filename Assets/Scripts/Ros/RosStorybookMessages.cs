﻿/* This file defines helper structs and enums for building and interpreting ROS messages. */

// Messages from the storybook to the controller.
public enum StorybookEventType {
    HELLO_WORLD = 0,
    SPEECH_ACE_RESULT = 1,
    REQUEST_ROBOT_FEEDBACK = 2,
    WORD_TAPPED = 3,
}

// Messages coming from the controller to the storybook.
// We will need to deal with each one by registering a handler.
public enum StorybookCommand {
    PING_TEST = 0, // No params.
    HIGHLIGHT_WORD = 1, // Params is which index word to highlight.
    HIGHLIGHT_SCENE_OBJECT = 2, // Params is which id scene object to highlight.
    HIGHLIGHT_STANZA = 3 // Params is which index stanza to highlight.
}

// Message type representing the high level state of the storybook, to be published at 10Hz.
public struct StorybookState {
    public bool audioPlaying;
    public bool isReading;
    public StorybookMode storybookMode; // StorybookMode.Explore or StorybookMode.Evaluate
    public string currentStory;
    public int numPages;
    public int currentStanzaIndex; // TODO: should add a flag because this value is not always meaningful, and it defaults to 0 which is misleading.
    public int currentTinkerTextIndex;
}

// Message type representing which page of the storybook is currently active.
public struct StorybookPageInfo {
    public string storyName;
    public int pageNumber; // 0-indexed, where 0 is the title page.
    public string[] stanzas;
    public StorybookSceneObject[] sceneObjects;
    public StorybookTinkerText[] tinkerTexts;
}

// To be nested inside of StorybookPageInfo.
// Represents info about a scene object on the page.
public struct StorybookSceneObject {
    public int id;
    public string label;
    public bool inText;
}

// To be nested inside of StorybookPageInfo.
// Represents info about a tinkertext on the page.
public struct StorybookTinkerText {
    public bool hasSceneObject;
    public int sceneObjectId;
    public string word;
}
    