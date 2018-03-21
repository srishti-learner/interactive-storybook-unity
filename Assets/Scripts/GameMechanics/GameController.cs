// This file contains the main Game Controller class.
//
// GameController handles the logic for the initial connection to ROS,
// as well as other metadata about the storybook interaction. GameController
// does not have to communicate over Ros, change behavior by setting the value
// of Constants.USE_ROS.
//
// GameController controls the high level progression of the story, and tells
// StoryManager which scenes to load.
//
// GameController is a singleton.

using System;
using System.Threading;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System.IO;
using NUnit.Framework.Internal;
using NUnit.Framework;
using System.Reflection.Emit;
using UnityEngine.Events;

public class GameController : MonoBehaviour {
    public GameObject testObjectRos;
    public GameObject testObjectRosSpeechace;

    // The singleton instance.
    public static GameController instance = null;

    // Task queue.
    private Queue<Action> taskQueue = new Queue<Action>();

    // UI GameObjects. Make public so that they can be attached in Unity.
    public Button landscapeNextButton;
    public Button landscapeBackButton;
    public Button landscapeFinishButton;
    public Button landscapeStartStoryButton;
    public Button landscapeBackToLibraryButton;
    public Button landscapeHomeButton; // In reader view, to exit the story.
    public Button landscapeToggleAudioButton;
    public Button landscapeDoneRecordingButton; // In reader view, stop recording, tell controller.

    public Button portraitNextButton;
    public Button portraitBackButton;
    public Button portraitFinishButton;
    public Button portraitStartStoryButton;
    public Button portraitBackToLibraryButton;
    public Button portraitHomeButton; // In reader view, to exit the story.
    public Button portraitToggleAudioButton;
    public Button portraitDoneRecordingButton; // In reader view, stop recording, tell controller.

    private Button nextButton;
    private Button backButton;
    private Button finishButton;
    private Button toggleAudioButton;
    private Button startStoryButton; // "Begin" button in title page.
    private Button backToLibraryButton; // Back button in title page.
    private Button homeButton; // Home icon in reader.
    private Button doneRecordingButton;

    public Button selectStoryButton;
    public GameObject loadingBar;

    public GameObject landscapePanel;
    public GameObject portraitPanel;

    // Objects for Library Screen, Story Selection and Mode Selection.
    public GameObject libraryPanel;

    // Objects for ROS connection.
    public GameObject rosPanel;
    public Button rosConnectButton;
    public Text rosStatusText;
    public Text rosInputText;
    public Text rosPlaceholderText;

    public Button enterLibraryButton;
    public Button readButton;
    public Button findMoreStoriesButton;

    // RosManager for handling connection to Ros, sending messages, etc.
    private RosManager rosManager;

    // Reference to SceneManager so we can load and manipulate story scenes.
    private StoryManager storyManager;

    // Reference to AssetDownloader.
    private AssetManager assetManager;
    private bool downloadedTitles = false;

    // Reference to AudioRecorder for when we need to record child and stream to SpeechACE.
    private AudioRecorder audioRecorder;
    // SpeechAceManager sends web requests and gets responses from SpeechACE.
    private SpeechAceManager speechAceManager;

    // List of stories to populate dropdown.
    private List<StoryMetadata> stories;

    public GameObject bookshelfPanel;
    private List<GameObject> libraryShelves;
    private List<GameObject> libraryBooks;
    private int selectedLibraryBookIndex = -1;

    // Some information about the current state of the storybook.
    private StoryMetadata currentStory;
    private ScreenOrientation orientation;
    // Stores the scene descriptions for the current story.
    private List<SceneDescription> storyPages;
    private int currentPageNumber = 0; // 0-indexed, index into this.storyPages, 0 is title page.

    void Awake()
    {
        // Enforce singleton pattern.
        if (instance == null)
        {
            instance = this;
        }
        else if (instance != this)
        {
            Logger.Log("duplicate GameController, destroying");
            Destroy(gameObject);
        }
        DontDestroyOnLoad(gameObject);

        // Do this in Awake() to avoid null references.
        StorybookStateManager.Init();
    }

    void Start()
    {
        // Set up all UI elements. (SetActive, GetComponent, etc.)
        // Get references to objects if necessary.
        Logger.Log("Game Controller start");
        this.landscapeNextButton.interactable = true;
        this.landscapeNextButton.onClick.AddListener(this.onNextButtonClick);
        this.portraitNextButton.interactable = true;
        this.portraitNextButton.onClick.AddListener(this.onNextButtonClick);

        this.landscapeBackButton.interactable = true;
        this.landscapeBackButton.onClick.AddListener(this.onBackButtonClick);
        this.portraitBackButton.interactable = true;
        this.portraitBackButton.onClick.AddListener(this.onBackButtonClick);

        this.landscapeFinishButton.interactable = true;
        this.landscapeFinishButton.onClick.AddListener(this.onFinishButtonClick);
        this.portraitFinishButton.interactable = true;
        this.portraitFinishButton.onClick.AddListener(this.onFinishButtonClick);

        this.landscapeBackToLibraryButton.onClick.AddListener(this.onBackToLibraryButtonClick);
        this.portraitBackToLibraryButton.onClick.AddListener(this.onBackToLibraryButtonClick);
        this.landscapeHomeButton.onClick.AddListener(this.onBackToLibraryButtonClick);
        this.portraitHomeButton.onClick.AddListener(this.onBackToLibraryButtonClick);

        this.landscapeStartStoryButton.onClick.AddListener(this.onNextButtonClick);
        this.portraitStartStoryButton.onClick.AddListener(this.onNextButtonClick);

        this.rosConnectButton.onClick.AddListener(this.onRosConnectButtonClick);
        this.enterLibraryButton.onClick.AddListener(this.onEnterLibraryButtonClick);
        this.selectStoryButton.onClick.AddListener(this.onReadButtonClick);

        this.landscapeToggleAudioButton.onClick.AddListener(this.toggleAudio);
        this.portraitToggleAudioButton.onClick.AddListener(this.toggleAudio);

        this.landscapeDoneRecordingButton.onClick.AddListener(this.onDoneRecordingButtonClick);
        this.portraitDoneRecordingButton.onClick.AddListener(this.onDoneRecordingButtonClick);

        this.readButton.onClick.AddListener(this.onReadButtonClick);

        // Update the sizing of all of the panels depending on the actual
        // screen size of the device we're on.
        this.resizePanelsOnStartup();

        this.storyPages = new List<SceneDescription>();

        this.storyManager = GetComponent<StoryManager>();
        this.assetManager = GetComponent<AssetManager>();
        this.audioRecorder = GetComponent<AudioRecorder>();
        this.speechAceManager = GetComponent<SpeechAceManager>();

        this.libraryBooks = new List<GameObject>();
        this.libraryShelves = new List<GameObject>();

        this.stories = new List<StoryMetadata>();
        this.initStories();

        // Either show the rosPanel to connect to ROS, or wait to go into story selection.

        if (Constants.USE_ROS) {
            this.setupRosScreen();
        }

        // TODO: figure out when to actually set this, should be dependent on game mode.
        this.storyManager.SetAutoplay(false);

    }

    // Update() is called once per frame.
    void Update() {
        // Pop tasks from the task queue and perform them.
        // Tasks are added from other threads, usually in response to ROS msgs.
        if (this.taskQueue.Count > 0) {
            try {
                Logger.Log("Got a task from queue in GameController");
                this.taskQueue.Dequeue().Invoke();
            } catch (Exception e) {
                Logger.LogError("Error invoking action on main thread!\n" + e);
            }
        }

        // Kinda sketch, make sure this happens once after everyone's Start
        // has been called.
        // TODO: Move some things in other classes to Awake and then call this in Start?
        if (!this.downloadedTitles && !Constants.USE_ROS) {
            this.downloadedTitles = true;
            // Set up the dropdown, load library panel.
            if (Constants.LOAD_ASSETS_LOCALLY) {
                this.setupStoryLibrary();
                this.showLibraryPanel(true);
            }
            else {
                this.downloadStoryTitles();
            }
        }
    }
    // Clean up.
    void OnApplicationQuit() {
        if (this.rosManager != null && this.rosManager.isConnected()) {
            // Stop the thread that's sending StorybookState messages.
            this.rosManager.StopSendingStorybookState();
            // Close the ROS connection cleanly.
            this.rosManager.CloseConnection();   
        }
    }

    // =============================
    // Setup functions.
    // =============================

    private void initStories() {
        // TODO: Read story metadata from the cloud here instead of hardcoding this stuff.
        // It should all be read from a single file, whose url is known.
        // In the future, consider using AmazonS3 API to manually read all the buckets.
        // Don't really want to do that now because it seems like more effort than worth.

        this.stories.Add(new StoryMetadata("a_dozen_dogs", 17, "landscape"));
        this.stories.Add(new StoryMetadata("at_bat", 9, "landscape"));
        this.stories.Add(new StoryMetadata("baby_pig_at_school", 15, "landscape"));
        this.stories.Add(new StoryMetadata("clifford_and_the_jet", 9, "landscape"));
        this.stories.Add(new StoryMetadata("freda_says_please", 17, "portrait"));
        this.stories.Add(new StoryMetadata("geraldine_first", 21, "landscape"));
        this.stories.Add(new StoryMetadata("henrys_happy_birthday", 29, "landscape"));
        this.stories.Add(new StoryMetadata("jane_and_jake_bake_a_cake", 15, "landscape"));
        this.stories.Add(new StoryMetadata("mice_on_ice", 15, "landscape"));
        this.stories.Add(new StoryMetadata("paws_and_claws", 14, "landscape"));
        this.stories.Add(new StoryMetadata("pete_the_cat_too_cool_for_school", 28, "portrait"));
        this.stories.Add(new StoryMetadata("the_biggest_cookie_in_the_world", 21, "landscape"));
        this.stories.Add(new StoryMetadata("the_hungry_toad", 15, "landscape"));
        this.stories.Add(new StoryMetadata("troll_tricks", 15, "portrait"));
        this.stories.Add(new StoryMetadata("who_hid_it", 9, "landscape"));


        // Other stories, commented out because they're not used in the study.
        //        this.stories.Add(new StoryMetadata("will_clifford_win", 9, "landscape"));
        //        this.stories.Add(new StoryMetadata("jazz_class", 12, "portrait"));
        //        this.stories.Add(new StoryMetadata("a_rain_forest_day", 15,"portrait"));
        //        this.stories.Add(new StoryMetadata("a_cub_can", 11,"portrait"));
    }

    // Called at startup if Constants.USE_ROS is true.
    private void setupRosScreen() {
        // Set placeholder text to be default IP.
        this.rosPlaceholderText.text = Constants.DEFAULT_ROSBRIDGE_IP;
        this.rosPanel.SetActive(true);
        this.landscapePanel.SetActive(false);
        this.portraitPanel.SetActive(false);
        this.libraryPanel.SetActive(false);
    }

    // Show human readable story names and pull title images when possible.
    private void setupStoryLibrary() {
        Logger.Log("setting up story library");
        int row = 0;
        int col = 0;
        for (int i = 0; i < this.stories.Count; i++) {
            if (col == 0) {
                this.libraryShelves.Add(Instantiate((GameObject)Resources.Load("Prefabs/Shelf")));
                this.libraryShelves[row].transform.SetParent(this.bookshelfPanel.transform);
                Util.UpdateShelfPosition(this.libraryShelves[row], row);
            }
            StoryMetadata story = this.stories[i];
            GameObject bookObject =
                Instantiate((GameObject)Resources.Load("Prefabs/LibraryBook"));
            LibraryBook libraryBook = bookObject.GetComponent<LibraryBook>();
            libraryBook.AddClickHandler(this.onLibraryBookClick(i, story));
            libraryBook.SetStory(story);
            libraryBook.SetSprite(this.assetManager.GetTitleSprite(story));
            bookObject.transform.SetParent(this.libraryShelves[row].transform);
            this.libraryBooks.Add(bookObject);
            col += 1;
            if (col / Constants.NUM_LIBRARY_COLS > 0) {
                col = col % Constants.NUM_LIBRARY_COLS;
                row += 1;
            }
        }
    }

    // ================================================================
    // Functions for starting a story and downloading assets.
    // ================================================================

    private void downloadStoryTitles() {
        List<string> storyNames = new List<string>();
        foreach (StoryMetadata story in this.stories) {
            storyNames.Add(story.GetName());
        }
        StartCoroutine(this.assetManager.DownloadTitlePages(storyNames,
        (Dictionary<string, Sprite> images, Dictionary<string, AudioClip> audios) => {
            // Callback for when download is complete.
            this.setupStoryLibrary();
            this.showLibraryPanel(true);
        }));
    }

    private Action onLibraryBookClick(int index, StoryMetadata story) {
        return () => {
            Logger.Log("Clicked book: " + index + " " + story.GetName());
            // Make all books normal size.
            if (this.selectedLibraryBookIndex >= 0) {
                this.libraryBooks[this.selectedLibraryBookIndex]
                    .GetComponent<LibraryBook>().ReturnToOriginalSize();
            }

            if (this.selectedLibraryBookIndex == index) {
                this.selectedLibraryBookIndex = -1;
                this.hideElement(this.readButton.gameObject);
            } else {
                this.selectedLibraryBookIndex = index;
                // Enlarge the appropriate library book.
                this.libraryBooks[index].GetComponent<LibraryBook>().Enlarge();
                // Show the Read button.
                this.showElement(this.readButton.gameObject);
            }
        };
    }
        
    private void startStory(StoryMetadata story) {
        this.currentStory = story;

        // Check if we need to download the json files.
        if (!Constants.LOAD_ASSETS_LOCALLY && !this.assetManager.JsonHasBeenDownloaded(story.GetName())) {
            this.showElement(this.loadingBar);
            StartCoroutine(this.assetManager.DownloadStoryJson(story, (_) => {
                List<StoryJson> storyJsons = this.assetManager.GetStoryJson(story);
                this.startStoryHelper(story, storyJsons);    
            }));
        } else {
            List<StoryJson> storyJsons = this.assetManager.GetStoryJson(story);
            this.startStoryHelper(story, storyJsons);
        }
    }

    private void startStoryHelper(StoryMetadata story, List<StoryJson> storyJsons) {
        // Sort to ensure pages are in order.
        storyJsons.Sort((s1, s2) => string.Compare(s1.GetName(), s2.GetName()));
        this.storyPages.Clear();
        // Figure out the orientation of this story and tell SceneDescription.
        this.orientation = story.GetOrientation();
        this.setOrientationButtons(this.orientation);
        foreach (StoryJson json in storyJsons) {
            this.storyPages.Add(new SceneDescription(json.GetText(), this.orientation));
        }
        // this.changeButtonText(this.nextButton, "Begin Story!");
        this.hideElement(this.backButton.gameObject);

        if (Constants.LOAD_ASSETS_LOCALLY ||
            this.assetManager.StoryHasBeenDownloaded(this.currentStory.GetName())) {
            // Either we load from memory or we've already cached a previous download.
            this.loadFirstPage();
        } else {
            // Choose to pass lists of strings instead of the SceneDescriptions objects,
            // unnecessary but just easier to avoid possibility of mutation down the line.
            List<string> imageFileNames = new List<string>();
            List<string> audioFileNames = new List<string>();
            foreach (SceneDescription d in this.storyPages) {
                imageFileNames.Add(d.storyImageFile);
                audioFileNames.Add(d.audioFile);
            }
            if (!this.assetManager.StoryHasBeenDownloaded(this.currentStory.GetName())) {
                this.hideElement(this.nextButton.gameObject);
                this.hideElement(this.toggleAudioButton.gameObject);
                StartCoroutine(this.assetManager.DownloadStoryAssets(this.currentStory.GetName(), imageFileNames,
                                                                    audioFileNames, this.onSelectedStoryDownloaded));
            } else {
                // The assets have already been downloaded, so just begin the story.
                this.loadFirstPage();
            }
        }
    }

    // Handle the newly downloaded sprites and audio clips.
    private void onSelectedStoryDownloaded(Dictionary<string, Sprite> sprites,
                                    Dictionary<string, AudioClip> audioClips) {
        this.loadFirstPage();
    }

    private void loadFirstPage() {
        this.rosManager.SendStorybookLoaded().Invoke();
        this.loadPageAndSendRosMessage(this.storyPages[this.currentPageNumber]);
        this.showLibraryPanel(false);
        this.hideElement(this.loadingBar);
        this.showElement(this.nextButton.gameObject);
        this.showElement(this.toggleAudioButton.gameObject);
        this.setOrientationView(this.orientation);
        // If in evaluate mode, don't show any navigation buttons.
        if (StorybookStateManager.GetState().storybookMode == StorybookMode.Evaluate) {
            this.showNavigationButtons(false);
        }
    }

    // ====================================
    // All button handlers.
    // ====================================

    private void onRosConnectButtonClick() {
        Logger.Log("Ros Connect Button clicked");
        string rosbridgeIp = Constants.DEFAULT_ROSBRIDGE_IP;
        // If user entered a different IP, use it, otherwise stick to default.
        if (this.rosInputText.text != "") {
            rosbridgeIp = this.rosInputText.text;
            Logger.Log("Trying to connect to roscore at " + rosbridgeIp);
        }
        if (this.rosManager == null || !this.rosManager.isConnected()) {
            this.rosManager = new RosManager(rosbridgeIp, Constants.DEFAULT_ROSBRIDGE_PORT, this);
            this.storyManager.SetRosManager(this.rosManager);
            if (this.rosManager.Connect()) {
                // If connection successful, update status text.
                this.rosStatusText.text = "Connected!";
                this.rosStatusText.color = Color.green;
                this.hideElement(this.rosConnectButton.gameObject);
                this.showElement(this.enterLibraryButton.gameObject);
                // Set up the command handlers, happens the first time connection is established.
                this.registerRosMessageHandlers();
                Thread.Sleep(1000); // Wait for a bit to make sure connection is established.
                this.rosManager.SendHelloWorldAction().Invoke();
                Logger.Log("Sent hello ping message");
            } else {
                this.rosStatusText.text = "Failed to connect, try again.";
                this.rosStatusText.color = Color.red;
            }
        } else {
            Logger.Log("Already connected to ROS, not trying to connect again");
        }
    }

    private void onEnterLibraryButtonClick() {
        // Prepares the assets for showing the library, and then displays the panel.
        this.downloadStoryTitles();
    }

    // Starts the story currently selected in the library.
    private void onReadButtonClick() {
        // Read the selected value of the story dropdown and start that story.
        LibraryBook selectedBook = this.libraryBooks[this.selectedLibraryBookIndex]
            .GetComponent<LibraryBook>();
        this.hideElement(readButton.gameObject);
        // Send ROS message.
        bool needsDownload = !this.assetManager.JsonHasBeenDownloaded(selectedBook.story.GetName());
        this.rosManager.SendStorybookSelected(needsDownload).Invoke();
        this.startStory(selectedBook.story);
        selectedBook.ReturnToOriginalSize();
    }

    // When user clicks button to go back to the library and exit the current story they're in.
    private void onBackToLibraryButtonClick() {
        this.storyManager.ClearPage();
        this.storyManager.audioManager.StopAudio();
        this.currentPageNumber = 0;
        this.setLandscapeOrientation();
        this.showLibraryPanel(true);
    }

    private void onNextButtonClick() {
        Logger.Log("Next Button clicked.");
        this.goToNextPage();
        if (this.currentPageNumber == 1) {
            // Special case, need to change the text and show the back button.
            // this.changeButtonText(this.nextButton, "Next Page");
            this.showElement(this.backButton.gameObject);
        }
        if (this.currentPageNumber == this.storyPages.Count - 1) {
            this.hideElement(this.nextButton.gameObject);
            this.showElement(this.finishButton.gameObject);
        }
	}

    private void onFinishButtonClick() {
        // Note: don't transition between modes automatically here anymore.
        // Just go back to the library.
        Logger.Log("Finish Button clicked.");
        this.finishStory();
        this.hideElement(this.finishButton.gameObject);
        this.showElement(this.nextButton.gameObject);
    }

    private void onBackButtonClick() {
        Logger.Log("Back Button clicked.");
        this.goToPrevPage();
        // Only if the button is actually clikced do we do the button navigation stuff.
        // Otherwise, we only call goToPrevPage() directly from a different place.
        if (this.currentPageNumber == 0) {
            // Hide the back button because we're at the beginning.
            this.hideElement(this.backButton.gameObject);
        }
        // Switch away from finish story to next button if we backtrack from the last page.
        if (this.currentPageNumber == this.storyPages.Count - 2) {
            this.hideElement(this.finishButton.gameObject);
            this.showElement(this.nextButton.gameObject);
        }
    }
        
    private void onDoneRecordingButtonClick() {
        Logger.Log("Done Recording Button Click");
        this.stopRecordingAndDoSpeechace();
    }
   

    // =================================================================
    // All ROS message handlers.
    // They should add tasks to the task queue.
    // =================================================================

    private void registerRosMessageHandlers() {
        this.rosManager.RegisterHandler(StorybookCommand.PING_TEST, this.onHelloWorldAckReceived);
        this.rosManager.RegisterHandler(StorybookCommand.HIGHLIGHT_WORD, this.onHighlightTinkerTextMessage);
        this.rosManager.RegisterHandler(StorybookCommand.HIGHLIGHT_SCENE_OBJECT, this.onHighlightSceneObjectMessage);
        this.rosManager.RegisterHandler(StorybookCommand.SHOW_NEXT_SENTENCE, this.onShowNextSentenceMessage);
        this.rosManager.RegisterHandler(StorybookCommand.BEGIN_RECORD, this.onBeginRecordMessage);
        this.rosManager.RegisterHandler(StorybookCommand.CANCEL_RECORD, this.onCancelRecordMessage);
        this.rosManager.RegisterHandler(StorybookCommand.SET_STORYBOOK_MODE, this.onSetStorybookModeMessage);
        this.rosManager.RegisterHandler(StorybookCommand.NEXT_PAGE, this.onNextPageMessage);
        this.rosManager.RegisterHandler(StorybookCommand.GO_TO_END_PAGE, this.onGoToEndPageMessage);
        this.rosManager.RegisterHandler(StorybookCommand.SHOW_LIBRARY_PANEL, this.onShowLibraryPanelMessage);
    }

    // PING_TEST
    private void onHelloWorldAckReceived(Dictionary<string, object> args) {
        // Sanity check from the ping test after tablet app starts up.
        Logger.Log("in hello world ack received in game controller, " + args["obj1"]);
    }

    private void onHighlightTinkerTextMessage(Dictionary<string, object> args) {
        int index = Convert.ToInt32(args["index"]);
        this.taskQueue.Enqueue(this.highlightTinkerText(index));
    }

    // HIGHLIGHT_WORD
    private Action highlightTinkerText(int index) {
        return () => {
            if (index < this.storyManager.tinkerTexts.Count && index >= 0) {
                this.storyManager.tinkerTexts[index].GetComponent<TinkerText>()
                    .Highlight().Invoke();   
            } else {
                Logger.Log("No word at index: " + index);
            }
        };
    }

    // HIGHLIGHT_SCENE_OBJECT
    private void onHighlightSceneObjectMessage(Dictionary<string, object> args) {
        int id = Convert.ToInt32(args["id"]);
        this.taskQueue.Enqueue(this.highlightSceneObject(id));
    }

    private Action highlightSceneObject(int id) {
        return () => {
            if (this.storyManager.sceneObjects.ContainsKey(id)) {
                this.storyManager.sceneObjects[id].GetComponent<SceneObjectManipulator>()
                    .Highlight(Constants.SCENE_OBJECT_HIGHLIGHT_COLOR).Invoke();   
            } else {
                Logger.Log("No scene object with id: " + id);
            }
        };
    }

    // SHOW_NEXT_SENTENCE
    private void onShowNextSentenceMessage(Dictionary<string, object> args) {
        // Assert that we are highlighting the appropriate sentence.
        // Need to cast better.
        Logger.Log("onShowNextSentenceMessage");
        if (Convert.ToInt32(args["index"]) != StorybookStateManager.GetState().evaluatingSentenceIndex + 1) {
            Logger.LogError("Sentence index doesn't match " + args["index"] + " " +
            StorybookStateManager.GetState().evaluatingSentenceIndex + 1);
            throw new Exception("Sentence index doesn't match, fail fast");
        }
        this.taskQueue.Enqueue(this.showNextSentence((bool)args["child_turn"], (bool)args["record"]));
    }

    private Action showNextSentence(bool childTurn, bool shouldRecord) {
        return () => {
            Logger.Log("Showing next sentence from inside task queue");
            if (StorybookStateManager.GetState().evaluatingSentenceIndex + 1 <
                this.storyManager.stanzaManager.GetNumSentences()) {
                StorybookStateManager.IncrementEvaluatingSentenceIndex();
                int newIndex = StorybookStateManager.GetState().evaluatingSentenceIndex;
                Color color = new Color();
                if (childTurn) {
                    color = Constants.CHILD_READ_TEXT_COLOR;
                } else {
                    color = Constants.JIBO_READ_TEXT_COLOR;
                }
                this.storyManager.stanzaManager.GetSentence(newIndex).FadeIn(color);
                if (newIndex - 1 >= 0) {
                    this.storyManager.stanzaManager.GetSentence(newIndex - 1).Highlight(Constants.GREY_TEXT_COLOR);
                }
                if (shouldRecord) {
                    this.recordAudioForCurrentSentence(newIndex).Invoke();
                }
            } else {
                throw new Exception("Cannot show sentence, index out of range");
            }
        };
    }

    // BEGIN_RECORD
    private void onBeginRecordMessage(Dictionary<string, object> args) {
        Logger.Log("onBeginRecordMessage");
        int sentenceIndex = StorybookStateManager.GetState().evaluatingSentenceIndex;
        this.taskQueue.Enqueue(this.recordAudioForCurrentSentence(sentenceIndex));
    }

    private Action recordAudioForCurrentSentence(int sentenceIndex) {
        return () => {
            Logger.Log("Recording audio from inside task queue");
            Sentence sentence = this.storyManager.stanzaManager.GetSentence(sentenceIndex);
            string text = sentence.GetSentenceText();
            // Approximately the length of the sentence, plus a little more.
            // int duration = Convert.ToInt32(sentence.GetDuration() * 1.33) + 1;
            Logger.Log("Start recording for sentence index " + sentenceIndex + " text: " + text);
            this.audioRecorder.StartRecording();            
        };
    }

    // CANCEL_RECORD
    private void onCancelRecordMessage(Dictionary<string, object> args) {
        Logger.Log("onCancelRecordMessage");
        this.taskQueue.Enqueue(this.cancelAndDiscardCurrentRecording());
    }

    private Action cancelAndDiscardCurrentRecording() {
        return () => {
            this.audioRecorder.EndRecording((clip) => {
                // Do nothing with the clip.
                Logger.Log("Recording ended");
            }); 
        };
    }

    // SET_STORYBOOK_MODE
    private void onSetStorybookModeMessage(Dictionary<string, object> args) {
        Logger.Log("onSetStorybookModeMessage");
        this.taskQueue.Enqueue(this.setStorybookMode(Convert.ToInt32(args["mode"])));
    }

    private Action setStorybookMode(int mode) {
        return () => {
            // Convert to StorybookMode.
            StorybookMode newMode = (StorybookMode)mode;
            StorybookStateManager.SetStorybookMode(newMode);

        };
    }

    // NEXT_PAGE
    private void onNextPageMessage(Dictionary<string, object> args) {
        Logger.Log("onNextPageMessage");
        this.taskQueue.Enqueue(this.goToNextPage);
    }

    // FINISH_STORY
    private void onGoToEndPageMessage(Dictionary<string, object> args) {
        Logger.Log("onGoToEndPageMessage");
        this.taskQueue.Enqueue(this.goToTheEndPage);
    }

    // SHOW_LIBRARY_PANEL
    private void onShowLibraryPanelMessage(Dictionary<string, object> args) {
        Logger.Log("onShowLibraryPanelMessage");
        this.taskQueue.Enqueue(() => {this.showLibraryPanel(true);});
    }

    // =================
    // Helpers.
    // =================
   
    // Separate the logic of showing buttons from actually moving pages.
    // In evaluate mode, we want to be able to instruct the tablet to navigate the pages
    // without the child needing to press any buttons.
    private void goToPrevPage() {
        this.currentPageNumber -= 1;
        if (this.currentPageNumber < 0) {
            // Fail fast.
            throw new Exception("Cannot go back any farther, already at beginning");
        }
        this.storyManager.ClearPage();
        StorybookStateManager.ResetEvaluatingSentenceIndex();
        // Explicitly send the state to make sure it gets sent before the page info does.
        this.rosManager.SendStorybookState();
        this.loadPageAndSendRosMessage(this.storyPages[this.currentPageNumber]);
    }

    private void goToNextPage() {
        this.currentPageNumber += 1;
        if (this.currentPageNumber > StorybookStateManager.GetState().numPages) {
            throw new Exception("Cannot go forward anymore, already at end " + this.currentPageNumber +  " " + StorybookStateManager.GetState().numPages);
        }
        this.storyManager.ClearPage();
        StorybookStateManager.ResetEvaluatingSentenceIndex();
        // Explicitly send the state to make sure it gets sent before the page info does.
        this.rosManager.SendStorybookState();
        this.loadPageAndSendRosMessage(this.storyPages[this.currentPageNumber]);
    }

    private void goToTheEndPage() {
        this.storyManager.ClearPage();
        this.storyManager.audioManager.StopAudio();
        // TODO: show the TheEnd page, and should show the home button to go back.
    }

    private void finishStory() {
        this.storyManager.ClearPage();
        this.storyManager.audioManager.StopAudio();
        this.currentPageNumber = 0;
        this.setLandscapeOrientation();
        this.showLibraryPanel(true);
        StorybookStateManager.SetStoryExited();
    }


    // When child is done speaking, get SpeechACE results and save the recording.
    private void stopRecordingAndDoSpeechace() {
        int sentenceIndex = StorybookStateManager.GetState().evaluatingSentenceIndex;
        string text = this.storyManager.stanzaManager.GetSentence(sentenceIndex).GetSentenceText();
        string tempFileName = this.currentPageNumber + "_" + sentenceIndex + ".wav";
        this.audioRecorder.EndRecording((clip) => {
            if (clip == null) {
                Logger.Log("Got null clip, means user pressed stop recording when no recording was active");
                return;
            }
            Logger.Log("Done recording, getting speechACE results and uploading file to S3...");
            // Tell controller we're done recording!
            this.rosManager.SendRecordAudioComplete(sentenceIndex).Invoke();
            // TODO: should also delete these audio files after we don't need them anymore.
            AudioRecorder.SaveAudioAtPath(tempFileName, clip);
            float duration = clip.length;
            StartCoroutine(this.speechAceManager.AnalyzeTextSample(
                tempFileName, text, (speechAceResult) => {
                    if (Constants.USE_ROS) {
                        this.rosManager.SendSpeechAceResultAction(sentenceIndex, text,
                            duration, speechAceResult).Invoke();
                    }
                    // If we want to replay for debugging, uncomment this.
                    // AudioClip loadedClip = AudioRecorder.LoadAudioLocal(fileName);
                    // this.storyManager.audioManager.LoadAudio(loadedClip);
                    // this.storyManager.audioManager.PlayAudio();
                    this.assetManager.S3UploadChildAudio(tempFileName);
                }));
        });
    }

    // Helper function to wrap together two actions:
    // (1) loading a page and (2) sending the StorybookPageInfo message over ROS.
    private void loadPageAndSendRosMessage(SceneDescription sceneDescription) {
        // Load the page.
        this.storyManager.LoadPage(sceneDescription);

        // Send the ROS message to update the controller about what page we're on now.
        StorybookPageInfo updatedInfo = new StorybookPageInfo();
        updatedInfo.storyName = this.currentStory.GetName();
        updatedInfo.pageNumber = this.currentPageNumber;
        updatedInfo.sentences = this.storyManager.stanzaManager.GetAllSentenceTexts();

        // Update state (will get automatically sent to the controller.
        StorybookStateManager.SetStorySelected(this.currentStory.GetName(),
            this.currentStory.GetNumPages());

        // Gather information about scene objects.
        StorybookSceneObject[] sceneObjects =
            new StorybookSceneObject[sceneDescription.sceneObjects.Length];
        for (int i = 0; i < sceneDescription.sceneObjects.Length; i++) {
            SceneObject so = sceneDescription.sceneObjects[i];
            StorybookSceneObject sso = new StorybookSceneObject();
            sso.id = so.id;
            sso.label = so.label;
            sso.inText = so.inText;
            sceneObjects[i] = sso;
        }
        updatedInfo.sceneObjects = sceneObjects;

        // Gather information about tinker texts.
        StorybookTinkerText[] tinkerTexts =
            new StorybookTinkerText[this.storyManager.tinkerTexts.Count];
        for (int i = 0; i < this.storyManager.tinkerTexts.Count; i++) {
            TinkerText tt = this.storyManager.tinkerTexts[i].GetComponent<TinkerText>();
            StorybookTinkerText stt = new StorybookTinkerText();
            stt.word = tt.word;
            stt.hasSceneObject = false;
            stt.sceneObjectId = -1;
            tinkerTexts[i] = stt;
        }
        foreach (Trigger trigger in sceneDescription.triggers) {
            if (trigger.type == TriggerType.CLICK_TINKERTEXT_SCENE_OBJECT) {
                tinkerTexts[trigger.args.textId].hasSceneObject = true;
                tinkerTexts[trigger.args.textId].sceneObjectId = trigger.args.sceneObjectId;
            }
        }
        updatedInfo.tinkerTexts = tinkerTexts;
       
        // Send the message.
        if (Constants.USE_ROS) {
            this.rosManager.SendStorybookPageInfoAction(updatedInfo);
        }
    }

    // TODO: Not sure if this will be necessary.
    private void toggleAudio() {
        // this.storyManager.ToggleAudio();
    }

    // ====================
    // UI Helpers.
    // ====================


    private void showLibraryPanel(bool show) {
        if (show) {
            this.libraryPanel.SetActive(true);
            this.landscapePanel.SetActive(false);
            this.portraitPanel.SetActive(false);
            this.rosPanel.SetActive(false);
        } else {
            this.libraryPanel.SetActive(false);
        }
    }

    private void showNavigationButtons(bool show) {
        if (show) {
            this.showElement(this.nextButton.gameObject);
            this.showElement(this.backButton.gameObject);
            this.showElement(this.homeButton.gameObject);
        } else {
            this.hideElement(this.nextButton.gameObject);
            this.hideElement(this.backButton.gameObject);
            this.hideElement(this.homeButton.gameObject);
        }
    }

    private void changeButtonText(Button button, string text) {
        button.GetComponentInChildren<Text>().text = text;
    }

    private void showElement(GameObject go) {
        go.SetActive(true);
    }

    private void hideElement(GameObject go) {
        go.SetActive(false);
    }

    private void resizePanelsOnStartup() {
        // Panels that need to be resized are landscapePanel, portraitPanel,
        // and libraryPanel.
        int width = Util.GetScreenWidth();
        int height = Util.GetScreenHeight();
        Vector2 landscape = new Vector2(width, height);
        Vector2 portrait = new Vector2(height, width);

        this.landscapePanel.GetComponent<RectTransform>().sizeDelta = landscape;
        this.portraitPanel.GetComponent<RectTransform>().sizeDelta = portrait;
        this.libraryPanel.GetComponent<RectTransform>().sizeDelta = landscape;
    }

    private void setOrientationButtons(ScreenOrientation o) {
        this.orientation = o;
        switch (o) {
            case ScreenOrientation.Landscape:
                this.setLandscapeOrientation();
                break;
            case ScreenOrientation.Portrait:
                this.setPortraitOrientation();
                break;
            default:
                Logger.LogError("No orientation: " + o);
                break;
        }
    }

    private void setOrientationView(ScreenOrientation o) {
        this.orientation = o;
        switch (o) {
        case ScreenOrientation.Landscape:
            this.portraitPanel.SetActive(false);
            this.landscapePanel.SetActive(true);
            break;
        case ScreenOrientation.Portrait:
            this.landscapePanel.SetActive(false);
            this.portraitPanel.SetActive(true);
            break;
        default:
            Logger.LogError("No orientation: " + o);
            break;
        }
    }

    private void setLandscapeOrientation() {
        Logger.Log("Changing to Landscape orientation");

        this.nextButton = this.landscapeNextButton;
        this.backButton = this.landscapeBackButton;
        this.finishButton = this.landscapeFinishButton;
        this.toggleAudioButton = this.landscapeToggleAudioButton;
        this.homeButton = this.landscapeHomeButton;
        this.startStoryButton = this.landscapeStartStoryButton;
        this.backToLibraryButton = this.landscapeBackToLibraryButton;
        this.doneRecordingButton = this.landscapeDoneRecordingButton;

        // TODO: is this necessary?
        Screen.orientation = ScreenOrientation.Landscape;
    }

    private void setPortraitOrientation() {
        Logger.Log("Changing to Portrait orientation");

        this.nextButton = this.portraitNextButton;
        this.backButton = this.portraitBackButton;
        this.finishButton = this.portraitFinishButton;
        this.toggleAudioButton = this.portraitToggleAudioButton;
        this.homeButton = this.portraitHomeButton;
        this.startStoryButton = this.portraitStartStoryButton;
        this.backToLibraryButton = this.portraitBackToLibraryButton;
        this.doneRecordingButton = this.portraitDoneRecordingButton;

        Screen.orientation = ScreenOrientation.Portrait;
    }

}
