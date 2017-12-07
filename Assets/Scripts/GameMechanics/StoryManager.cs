// StoryManager loads a scene based on a SceneDescription, including loading
// images, audio files, and drawing colliders and setting up callbacks to
// handle trigger events. StoryManager uses methods in TinkerText and
// SceneObjectManipulator for setting up these callbacks.

using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using System;
using System.Linq;
using System.Collections.Generic;

public class StoryManager : MonoBehaviour {

    // We may want to call methods on GameController or add to the task queue.
    public GameController gameController;
    public StoryAudioManager audioManager;

	public GameObject portraitGraphicsPanel;
    public GameObject portraitTextPanel;
    public GameObject landscapeGraphicsPanel;
    public GameObject landscapeTextPanel;
	public GameObject landscapeWideGraphicsPanel;
	public GameObject landscapeWideTextPanel;

    public GameObject portraitTitlePanel;
    public GameObject landscapeTitlePanel;

    private bool autoplayAudio = true;

    // Used for internal references.
    private GameObject graphicsPanel;
    private GameObject textPanel;
    private GameObject titlePanel;
    private GameObject currentStanza;

    private float graphicsPanelWidth;
    private float graphicsPanelHeight;
    private float graphicsPanelAspectRatio;
    private float titlePanelAspectRatio;

    // Variables for loading TinkerTexts.
    private float STANZA_SPACING = 20; // Matches Prefab.
    // private float MIN_TINKER_TEXT_WIDTH = TinkerText.MIN_WIDTH;

    // These all need to be set after we determine the screen size.
    // They are set in initPanelSizesOnStartup() and used in resetPanelSizes().
    private float LANDSCAPE_GRAPHICS_WIDTH;
    private float LANDSCAPE_TEXT_WIDTH;
    private float LANDSCAPE_HEIGHT;
    private float PORTRAIT_GRAPHICS_HEIGHT;
    private float PORTRAIT_TEXT_HEIGHT;
    private float PORTRAIT_WIDTH;
    private float LANDSCAPE_WIDE_GRAPHICS_HEIGHT;
    private float LANDSCAPE_WIDE_TEXT_HEIGHT;
    private float LANDSCAPE_WIDE_WIDTH;

    // Variables for taking care of TinkerText placing and stanza breaking.
    private float remainingStanzaWidth = 0; // For loading TinkerTexts.
    private bool prevWordEndsStanza = false; // Know when to start new stanza.
    private List<GameObject> tinkerTextPhraseBuffer;
    private GameObject lastStartStanza;
    private bool shouldUpdateStanzaTimes = false;
    private bool breakAtNextOpportunity = false;

    // Array of sentences where each sentence is an array of stanzas.
    private List<Sentence> sentences;

    // Dynamically created Stanzas.
    private List<GameObject> stanzas;
    // Dynamically created TinkerTexts specific to this scene.
    private List<GameObject> tinkerTexts;
    // Dynamically created SceneObjects, keyed by their id.
    private Dictionary<int, GameObject> sceneObjects;
    private Dictionary<string, List<int>> sceneObjectsLabelToId;
    // The image we loaded for this scene.
    private GameObject storyImage;
    // Need to know the actual dimensions of the background image, so that we
    // can correctly place new SceneObjects on the background.
    private float storyImageWidth;
    private float storyImageHeight;
    // The (x,y) coordinates of the upper left corner of the image in relation
    // to the upper left corner of the encompassing GameObject.
    private float storyImageX;
    private float storyImageY;
    // Ratio of the story image to the original texture size.
    private float imageScaleFactor;
    private DisplayMode displayMode;

    void Start() {
        Logger.Log("StoryManager start");

        this.tinkerTexts = new List<GameObject>();
        this.stanzas = new List<GameObject>();
        this.sceneObjects = new Dictionary<int, GameObject>();
        this.sceneObjectsLabelToId = new Dictionary<string, List<int>>();

        this.tinkerTextPhraseBuffer = new List<GameObject>();

        this.initPanelSizesOnStartup();
    }

    void Update() {
        // Update whether or not we are accepting user interaction.
        Stanza.allowSwipe = !this.audioManager.IsPlaying();
    }

    // Main function to be called by GameController.
    // Passes in a description received over ROS or hardcoded.
    // LoadScene is responsible for loading all resources and putting them in
    // place, and attaching callbacks to created GameObjects, where these
    // callbacks involve functions from SceneManipulatorAPI.
    public void LoadPage(SceneDescription description) {
        this.setDisplayMode(description.displayMode);
        this.resetPanelSizes();    
        // Load audio.
        this.audioManager.LoadAudio(description.audioFile);

        if (description.isTitle) {
            // Special case for title page.
            // No TinkerTexts, and image takes up a larger space.
            this.loadTitlePage(description);
        } else {
            // Load image.
            this.loadImage(description.storyImageFile);

            // Load all words as TinkerText. Start at beginning of a stanza.
            this.remainingStanzaWidth = 0;

            List<string> textWords =
                new List<string>(description.text.Split(' '));
            // Need to remove any empty or only punctuation words.
            textWords.RemoveAll(String.IsNullOrEmpty);
            List<string> filteredTextWords = new List<string>();
            foreach (string word in textWords) {
                if (word.Length == 1 && Util.punctuation.Contains(word)) {
                    filteredTextWords[filteredTextWords.Count - 1] += word;
                } else {
                    filteredTextWords.Add(word);
                }
            }
            if (filteredTextWords.Count != description.timestamps.Length) {
                Logger.LogError("textWords doesn't match timestamps length " +
                                filteredTextWords.Count.ToString() + " " + 
                               description.timestamps.Length.ToString());
            }
            if (this.lastStartStanza != null) {
                Logger.LogError("lastStartStanza should be null");
            }
            for (int i = 0; i < filteredTextWords.Count; i++)
            {
                this.loadTinkerText(filteredTextWords[i], description.timestamps[i],
                                    i == filteredTextWords.Count - 1);
            }
            // Set end timestamp of last stanza (edge case).
            this.stanzas[this.stanzas.Count - 1].GetComponent<Stanza>().SetEndTimestamp(
                this.tinkerTexts[this.tinkerTexts.Count - 1]
                .GetComponent<TinkerText>().audioEndTime);
            // Load audio triggers for TinkerText.
            this.loadAudioTriggers();
        }

        // Load all scene objects.
        foreach (SceneObject sceneObject in description.sceneObjects) {
            this.loadSceneObject(sceneObject);
        }

        // Sort scene objects by size (smallest on top).
        this.sortSceneObjectLayering();

        // Load triggers.
        foreach (Trigger trigger in description.triggers) {
            this.loadTrigger(trigger);
        }

        if (this.autoplayAudio) {
            this.audioManager.PlayAudio();
        }
    }

    // Begin playing the audio. Can be called by GameController in response
    // to UI events like button clicks or swipes.
    public void ToggleAudio() {
        this.audioManager.ToggleAudio();
    }

    private void loadTitlePage(SceneDescription description) {
        // Load the into the title panel without worrying about anything except
        // for fitting the space and making the aspect ratio correct.
        // Basically the same as first half of loadImage() function.
        string imageFile = description.storyImageFile;
        GameObject newObj = new GameObject();
        newObj.AddComponent<Image>();
        newObj.AddComponent<AspectRatioFitter>();
        newObj.transform.SetParent(this.titlePanel.transform, false);
        newObj.transform.localPosition = Vector3.zero;
        newObj.GetComponent<AspectRatioFitter>().aspectMode =
                  AspectRatioFitter.AspectMode.FitInParent;
        newObj.GetComponent<AspectRatioFitter>().aspectRatio =
                  this.titlePanelAspectRatio;
        Sprite sprite = Util.GetStorySprite(imageFile);
        newObj.GetComponent<Image>().sprite = sprite;
        newObj.GetComponent<Image>().preserveAspect = true;
        this.storyImage = newObj;
    }

    // Argument imageFile should be something like "the_hungry_toad_01" and then
    // this function will find it in the Resources directory and load it.
    private void loadImage(string imageFile) {
        string storyName = Util.FileNameToStoryName(imageFile);
        GameObject newObj = new GameObject();
        newObj.AddComponent<Image>();
        newObj.AddComponent<AspectRatioFitter>();
        newObj.GetComponent<AspectRatioFitter>().aspectMode =
          AspectRatioFitter.AspectMode.FitInParent;
        string fullImagePath = "StoryPages/" + storyName + "/" + imageFile;
        Sprite sprite = Resources.Load<Sprite>(fullImagePath);
        newObj.GetComponent<Image>().sprite = sprite;
        newObj.GetComponent<Image>().preserveAspect = true;
        newObj.transform.SetParent(this.graphicsPanel.transform, false);
        newObj.transform.localPosition = Vector3.zero;
        // Figure out sizing so that later scene objects can be loaded relative
        // to the background image for accurate overlay.
        Texture texture = Resources.Load<Texture>(fullImagePath);
        float imageAspectRatio = (float)texture.width / (float)texture.height;
        newObj.GetComponent<AspectRatioFitter>().aspectRatio =
          imageAspectRatio;
        // TODO: If height is constraining factor, then use up all possible
        // width by pushing the image over, only in landscape mode though.
        // Do the symmetric thing in portrait mode if width is constraining.
        if (imageAspectRatio > this.graphicsPanelAspectRatio) {
            // Width is the constraining factor.
            this.storyImageWidth = this.graphicsPanelWidth;
            this.storyImageHeight = this.graphicsPanelWidth / imageAspectRatio;
            this.storyImageX = 0;
            this.storyImageY = 
                -(this.graphicsPanelHeight - this.storyImageHeight) / 2;
        } else {
            // Height is the constraining factor.
            this.storyImageHeight = this.graphicsPanelHeight;
            this.storyImageWidth = this.graphicsPanelHeight * imageAspectRatio;
            if (this.displayMode == DisplayMode.Landscape) {
                float widthDiff = this.graphicsPanelWidth - this.storyImageWidth;
                this.graphicsPanelWidth = this.storyImageWidth;
                this.graphicsPanel.GetComponent<RectTransform>().sizeDelta =
                    new Vector2(this.storyImageWidth, this.storyImageHeight);
                Vector2 currentTextPanelSize =
                    this.textPanel.GetComponent<RectTransform>().sizeDelta;
                this.textPanel.GetComponent<RectTransform>().sizeDelta =
                    new Vector2(currentTextPanelSize.x + widthDiff,
                                currentTextPanelSize.y);   
            }
            this.storyImageY = 0;
            this.storyImageX = 
                (this.graphicsPanelWidth - this.storyImageWidth) / 2;
        }

        this.imageScaleFactor = this.storyImageWidth / texture.width;
        this.storyImage = newObj;
    }

    // Add a new TinkerText for the given word.
    private void loadTinkerText(string word, AudioTimestamp timestamp, bool isLastWord) {
        //Logger.Log("adding word: " + word);
        if (word.Length == 0) {
            return;
        }
		GameObject newTinkerText =
            Instantiate((GameObject)Resources.Load("Prefabs/TinkerText"));
        newTinkerText.GetComponent<TinkerText>()
             .Init(this.tinkerTexts.Count, word, timestamp, isLastWord);
        // Figure out how wide the TinkerText wants to be, then decide if
        // we need to make a new stanza.
        GameObject newText = newTinkerText.GetComponent<TinkerText>().text;
        float preferredWidth =
            LayoutUtility.GetPreferredWidth(
                newText.GetComponent<RectTransform>()
            );
        // Commented out to prevent text from being unevenly spaced.
        // preferredWidth = Math.Max(preferredWidth, this.MIN_TINKER_TEXT_WIDTH);
        // Add new stanza if no more room, and remove all TinkerTexts in the buffer from previous
        // stanza and instead re-add them to this stanza.
        if (preferredWidth > this.remainingStanzaWidth || (this.breakAtNextOpportunity && this.prevWordEndsStanza))
        {
            if (this.breakAtNextOpportunity && this.prevWordEndsStanza) {
                this.breakAtNextOpportunity = false;
            }
            this.shouldUpdateStanzaTimes = false;
            // Tell this tinkerText it's first in the stanza, important for audio playback,
            // because otherwise the highlighting occurs weirdly when stanzas play.
            GameObject newStanza =
                Instantiate((GameObject)Resources.Load("Prefabs/StanzaPanel"));
            newStanza.transform.SetParent(this.textPanel.transform, false);
            newStanza.GetComponent<Stanza>().Init(
                this.audioManager,
                this.textPanel.GetComponent<RectTransform>().position
            );

            // Reset the remaining stanza width.
            this.remainingStanzaWidth =
                    this.textPanel.GetComponent<RectTransform>().sizeDelta.x;
            float newStanzaTimestamp = timestamp.start;
            // Case where we need to copy over some phrases.
            if (!this.prevWordEndsStanza && this.tinkerTextPhraseBuffer.Count > 0)
            {
                // Find the previous timestamp.
                int prevPhraseLastTinkerTextIndex =
                    this.tinkerTexts.Count - 1 - this.tinkerTextPhraseBuffer.Count;
                //Logger.Log("need to copy some phrases over");
                if (prevPhraseLastTinkerTextIndex >= 0)
                {
                    //Logger.Log("moving phrase to next stanza");
                    // Move all the TinkerTexts in the phrase buffer to the new stanza.
                    // This is to ensure that no phrases are broken across multiple stanzas.
                    for (int i = 0; i < this.tinkerTextPhraseBuffer.Count; i++)
                    {
                        this.moveTinkerTextToStanza(this.tinkerTextPhraseBuffer[i], newStanza);
                        if (i == 0)
                        {
                            this.tinkerTextPhraseBuffer[i].GetComponent<TinkerText>().SetFirstInStanza();
                        }
                    }

                    // This means we can move the last phrase to the new stanza successfully.
                    newStanzaTimestamp =
                        this.tinkerTexts[prevPhraseLastTinkerTextIndex].GetComponent<TinkerText>()
                            .audioEndTime;
                    this.shouldUpdateStanzaTimes = true;

                }
                else
                {
                    // This is the case where we have to continue the phrase and break it.
                    // Make the new stanza unclickable since it's the middle of a phrase.
                    //Logger.Log("breaking, unclickable on current stanza, number already is " + this.stanzas.Count);
                    this.breakAtNextOpportunity = true;
                    newStanza.GetComponent<Stanza>().specificStanzaAllowSwipe = false;
                    this.tinkerTextPhraseBuffer.Clear();
                }
                // Clear the buffer.
                this.tinkerTextPhraseBuffer.Clear();
            }
            else
            {
                // No phrases to copy over, this is either a clean start or a lucky break position.
                newTinkerText.GetComponent<TinkerText>().SetFirstInStanza();
                this.shouldUpdateStanzaTimes = true;
                // For the first word, set up lastStartStanza.
                if (this.lastStartStanza == null)
                {
                    this.lastStartStanza = newStanza;
                    this.shouldUpdateStanzaTimes = false; // Because there's no previous stanza.
                }
            }

            // Book keeping.
            newStanza.GetComponent<Stanza>().index = this.stanzas.Count;
            this.stanzas.Add(newStanza);
            this.currentStanza = newStanza;

            // Set the end time of previous stanza and start time of the new
            // stanza we're adding.
            if (this.shouldUpdateStanzaTimes)
            {
                Logger.Log("try to update stanza times");
                if (this.lastStartStanza != null)
                {
                    this.lastStartStanza.GetComponent<Stanza>().SetEndTimestamp(
                    newStanzaTimestamp);
                }
                this.currentStanza.GetComponent<Stanza>().SetStartTimestamp(
                    newStanzaTimestamp);
                this.lastStartStanza = newStanza;
            }

        }
        // Initialize the TinkerText width correctly.
        // Set new TinkerText parent to be the stanza.
        newTinkerText.GetComponent<TinkerText>().SetWidth(preferredWidth);
        this.moveTinkerTextToStanza(newTinkerText, this.currentStanza);
        this.tinkerTexts.Add(newTinkerText);
        this.tinkerTextPhraseBuffer.Add(newTinkerText);
        this.prevWordEndsStanza = Util.WordShouldEndStanza(word);
        // Clear the buffer on end of phrase.
        if (this.prevWordEndsStanza) {
            this.tinkerTextPhraseBuffer.Clear();
        }
    }

    // Helper that simply moves an existing TinkerText game object into the current stanza.
    private void moveTinkerTextToStanza(GameObject tinkerText, GameObject stanza) {
        tinkerText.transform.SetParent(stanza.transform, false);
        // Subtract the width of this TinkerText and the standard stanza spacing.
        this.remainingStanzaWidth -= tinkerText.GetComponent<RectTransform>().sizeDelta.x;
        this.remainingStanzaWidth -= STANZA_SPACING;

    }

    // Adds a SceneObject to the story scene.
    private void loadSceneObject(SceneObject sceneObject) {
        // Allow multiple scene objects per label as long as we believe that they are referring to
        // different objects.
        Logger.Log("adding object " + sceneObject.label);
        if (this.sceneObjectsLabelToId.ContainsKey(sceneObject.label)) {
            // Check for overlap.
            Logger.Log("checking for " + sceneObject.label);
            foreach (int existingObject in this.sceneObjectsLabelToId[sceneObject.label]) {
                if (Util.RefersToSameObject(
                        sceneObject.position,
                    this.sceneObjects[existingObject].GetComponent<SceneObjectManipulator>().position)) {
                    Logger.Log("detected overlap");
                    return;
                }
            }
        }
        // Save this id under its label.
        if (!this.sceneObjectsLabelToId.ContainsKey(sceneObject.label)) {
            this.sceneObjectsLabelToId[sceneObject.label] = new List<int>();
        }
        this.sceneObjectsLabelToId[sceneObject.label].Add(sceneObject.id);

        GameObject newObj = 
            Instantiate((GameObject)Resources.Load("Prefabs/SceneObject"));
        newObj.transform.SetParent(this.graphicsPanel.transform, false);
        newObj.GetComponent<RectTransform>().SetAsLastSibling();
        // Set the position.
        SceneObjectManipulator manip =
            newObj.GetComponent<SceneObjectManipulator>();
        Position pos = sceneObject.position;
        manip.id = sceneObject.id;
        manip.label = sceneObject.label;
        manip.position = pos; 
        manip.MoveToPosition(
            new Vector3(this.storyImageX + pos.left * this.imageScaleFactor,
                        this.storyImageY - pos.top * this.imageScaleFactor)
        )();
        manip.ChangeSize(
            new Vector2(pos.width * this.imageScaleFactor,
                        pos.height * this.imageScaleFactor)
        )();
        // Add a dummy handler to check things.
        manip.AddClickHandler(() =>
        {
            Logger.Log("SceneObject clicked " +
                       manip.label);
        });
        // TODO: If sceneObject.inText is false, set up whatever behavior we
        // want for these words.
        if (!sceneObject.inText) {
            manip.AddClickHandler(() =>
            {
                Logger.Log("Not in text! " + manip.label);
            });
        }
        // Name the GameObject so we can inspect in the editor.
        newObj.name = sceneObject.label;
        this.sceneObjects[sceneObject.id] = newObj;
    }

    // Places smallest scene objects higher up in the z direction.
    private void sortSceneObjectLayering() {
        Dictionary<int, GameObject>.KeyCollection idKeys = this.sceneObjects.Keys;
        List<int> ids = new List<int>();
        foreach (int id in idKeys) {
            ids.Add(id);
        }
        ids.Sort((id1, id2) => {
            Position pos1 = this.sceneObjects[id1].GetComponent<SceneObjectManipulator>().position;
            Position pos2 = this.sceneObjects[id2].GetComponent<SceneObjectManipulator>().position;
            return pos2.width * pos2.height - pos1.width * pos1.height;
        });
        // Now that they are in reverse sorted order, move them to the front in sequence.
        foreach (int id in ids) {
            GameObject sceneObject = this.sceneObjects[id];
            sceneObject.GetComponent<RectTransform>().SetAsLastSibling();
        }
    }


    // Sets up a trigger between TinkerTexts and SceneObjects.
    private void loadTrigger(Trigger trigger) {
        switch (trigger.type) {
            case TriggerType.CLICK_TINKERTEXT_SCENE_OBJECT:
                // It's possible this sceneObject was not added because we found that it
                // overlapped with a previous object. This is fine, just skip it.
                if (!this.sceneObjects.ContainsKey(trigger.args.sceneObjectId)) {
                    return;
                }
                SceneObjectManipulator manip = 
                    this.sceneObjects[trigger.args.sceneObjectId]
                    .GetComponent<SceneObjectManipulator>();
                TinkerText tinkerText = this.tinkerTexts[trigger.args.textId]
                                            .GetComponent<TinkerText>();
                Action action = manip.Highlight(new Color(0, 1, 1, 60f / 255));
                tinkerText.AddClickHandler(action);
                manip.AddClickHandler(tinkerText.Highlight());
                break;
            default:
                Logger.LogError("Unknown TriggerType: " +
                                trigger.type.ToString());
                break;
                
        }
    }

    // Sets up a timestamp trigger on the audio manager.
    private void loadAudioTriggers() {
        foreach (GameObject t in this.tinkerTexts) {
            TinkerText tinkerText = t.GetComponent<TinkerText>();
            this.audioManager.AddTrigger(tinkerText.audioStartTime,
                                         tinkerText.OnStartAudioTrigger,
                                         tinkerText.isFirstInStanza);
            this.audioManager.AddTrigger(
                tinkerText.audioEndTime, tinkerText.OnEndAudioTrigger); 
        }
    }

    // Called by GameController to change whether we autoplay o not.
    public void SetAutoplay(bool newValue) {
        this.autoplayAudio = newValue;
    } 

    // Called by GameController when we should remove all elements we've added
    // to this page (usually in preparration for the creation of another page).
    public void ClearPage() {
        // Destroy stanzas.
        foreach (GameObject stanza in this.stanzas) {
            Destroy(stanza);
        }
        this.stanzas.Clear();
        // Destroy TinkerText objects we have a reference to, and reset list.
        foreach (GameObject tinkertext in this.tinkerTexts) {
            Destroy(tinkertext);
        }
        this.tinkerTexts.Clear();
        // Destroy SceneObjects we have a reference to, and empty dictionary.
        foreach (KeyValuePair<int,GameObject> obj in this.sceneObjects) {
            Destroy(obj.Value);
        }
        this.sceneObjects.Clear();
        this.sceneObjectsLabelToId.Clear();
        // Remove all images.
        Destroy(this.storyImage.gameObject);
        this.storyImage = null;
        // Remove audio triggers.
        this.audioManager.ClearTriggersAndReset();
        this.prevWordEndsStanza = false;
        this.lastStartStanza = null;
    }

    // Update the display mode. We need to update our internal references to
    // textPanel and graphicsPanel.
    private void setDisplayMode(DisplayMode newMode) {
        if (this.displayMode != newMode) {
            this.displayMode = newMode;
            if (this.graphicsPanel != null) {
                this.graphicsPanel.SetActive(false);
                this.textPanel.SetActive(false);
                this.titlePanel.SetActive(false);
            }
            switch (this.displayMode)
            {
                case DisplayMode.Landscape:
                    this.graphicsPanel = this.landscapeGraphicsPanel;
                    this.textPanel = this.landscapeTextPanel;
                    this.titlePanel = this.landscapeTitlePanel;
                    break;
                case DisplayMode.LandscapeWide:
                    this.graphicsPanel = this.landscapeWideGraphicsPanel;
                    this.textPanel = this.landscapeWideTextPanel;
                    this.titlePanel = this.landscapeTitlePanel;
                    break;
                case DisplayMode.Portrait:
                    this.graphicsPanel = this.portraitGraphicsPanel;
                    this.textPanel = this.portraitTextPanel;
                    this.titlePanel = this.portraitTitlePanel;
                    // Resize back to normal.
                    this.graphicsPanel.GetComponent<RectTransform>().sizeDelta =
                            new Vector2(this.PORTRAIT_WIDTH,
                                        this.PORTRAIT_GRAPHICS_HEIGHT);
                    break;
                default:
                    Logger.LogError("unknown display mode " + newMode);
                    break;
            }
            this.graphicsPanel.SetActive(true);
            this.textPanel.SetActive(true);
            this.titlePanel.SetActive(true);
            Vector2 rect =
                this.graphicsPanel.GetComponent<RectTransform>().sizeDelta;
            this.graphicsPanelWidth = (float)rect.x;
            this.graphicsPanelHeight = (float)rect.y;
            this.graphicsPanelAspectRatio =
                this.graphicsPanelWidth / this.graphicsPanelHeight;
            rect = this.titlePanel.GetComponent<RectTransform>().sizeDelta;
            this.titlePanelAspectRatio = (float)rect.x / (float)rect.y;
        }

    }

    // Called once on startup to size the layout panels correctly. Saves the
    // new values as constants so that resetPanelSizes() can use them to
    // dynamically resize the panels between scenes.
    private void initPanelSizesOnStartup() {
        float landscapeWidth = (float)Util.GetScreenWidth() - 100;
        float landscapeHeight = (float)Util.GetScreenHeight() - 330f; // Subtract buttons
        float portraitWidth = (float)Util.GetScreenHeight() - 100f;
        float portraitHeight = (float)Util.GetScreenWidth() - 330f; // Subtract buttons.

        this.LANDSCAPE_GRAPHICS_WIDTH =
                Constants.LANDSCAPE_GRAPHICS_WIDTH_FRACTION * landscapeWidth;
        this.LANDSCAPE_TEXT_WIDTH = landscapeWidth - this.LANDSCAPE_GRAPHICS_WIDTH;
        this.LANDSCAPE_HEIGHT = landscapeHeight;
        Util.SetSize(
            this.landscapeGraphicsPanel,
            new Vector2(this.LANDSCAPE_GRAPHICS_WIDTH, this.LANDSCAPE_HEIGHT));
        Util.SetSize(
            this.landscapeTextPanel,
            new Vector2(this.LANDSCAPE_TEXT_WIDTH, this.LANDSCAPE_HEIGHT));

        this.PORTRAIT_GRAPHICS_HEIGHT =
                Constants.PORTRAIT_GRAPHICS_HEIGHT_FRACTION * portraitHeight;
        this.PORTRAIT_TEXT_HEIGHT = portraitHeight - this.PORTRAIT_GRAPHICS_HEIGHT;
        this.PORTRAIT_WIDTH = portraitWidth;
        Util.SetSize(
            this.portraitGraphicsPanel,
            new Vector2(this.PORTRAIT_WIDTH, this.PORTRAIT_GRAPHICS_HEIGHT));
        Util.SetSize(
            this.portraitTextPanel,
            new Vector2(this.PORTRAIT_WIDTH, this.PORTRAIT_TEXT_HEIGHT));
        
        this.LANDSCAPE_WIDE_GRAPHICS_HEIGHT =
                Constants.LANDSCAPE_WIDE_GRAPHICS_HEIGHT_FRACTION * landscapeHeight;
        this.LANDSCAPE_WIDE_TEXT_HEIGHT =
                landscapeHeight - this.LANDSCAPE_WIDE_GRAPHICS_HEIGHT;
        this.LANDSCAPE_WIDE_WIDTH = landscapeWidth;
        Util.SetSize(
            this.landscapeWideGraphicsPanel,
            new Vector2(this.LANDSCAPE_WIDE_WIDTH, this.LANDSCAPE_WIDE_GRAPHICS_HEIGHT));
        Util.SetSize(
            this.landscapeWideTextPanel,
            new Vector2(this.LANDSCAPE_WIDE_WIDTH, this.LANDSCAPE_WIDE_TEXT_HEIGHT));

        // And the title panels.
        Util.SetSize(this.landscapeTitlePanel, new Vector2(landscapeWidth, landscapeHeight));
        Util.SetSize(this.portraitTitlePanel, new Vector2(portraitWidth, portraitHeight));
    }

    private void resetPanelSizes() {
        Vector2 graphicsSize = new Vector2();
        Vector2 textSize = new Vector2();
        switch(this.displayMode) {
            case DisplayMode.Landscape:
                graphicsSize = new Vector2(this.LANDSCAPE_GRAPHICS_WIDTH, this.LANDSCAPE_HEIGHT);
                textSize = new Vector2(this.LANDSCAPE_TEXT_WIDTH, this.LANDSCAPE_HEIGHT);
                break;
            case DisplayMode.Portrait:
                graphicsSize = new Vector2(this.PORTRAIT_WIDTH, this.PORTRAIT_GRAPHICS_HEIGHT);
                textSize = new Vector2(this.PORTRAIT_WIDTH, this.PORTRAIT_TEXT_HEIGHT);
                break;
            case DisplayMode.LandscapeWide:
                graphicsSize = new Vector2(this.LANDSCAPE_WIDE_WIDTH,
                                           this.LANDSCAPE_WIDE_GRAPHICS_HEIGHT);
                textSize = new Vector2(this.LANDSCAPE_WIDE_WIDTH, this.PORTRAIT_TEXT_HEIGHT);
                break;
            default:
                Logger.LogError("Unknown display mode: " + displayMode.ToString());
                break;
        }
        Util.SetSize(this.graphicsPanel, graphicsSize);
        Util.SetSize(this.textPanel, textSize);
    }

}
