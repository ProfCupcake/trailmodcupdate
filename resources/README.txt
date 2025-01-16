Hi! Welcome to the Natural Trail Mod Readme and thanks for downloading.

As of 1.0.2 Natural Trail Mod has a config file now! 
For the uninitiated, the config file will appear in your VintageStoryData/ModConfig AFTER you run your game and load a map with the mod loaded for the first time.

If you can't find it there, look in VintageStory/ModConfig.

Every time you install a new version of the mod, make sure you look at your config, as options may have changed/moved around, 
I can't promise your settings will be unaltered by the update, so just give it a quick look over so we can both sleep at night.
If you are experiencing any kind of weird behavior, always check your config first, as a rule of thumb.

A word to the wise, if you intend to set your own custom trail values, please do so in a new world or a backed-up world, you can really tear up the landscape by setting these values wrong, so it's best to do it in a world you can afford to mess up.

So what's in the config?

GENERAL SETTINGS
dirtRoadsOnly                      true/false 	//Enables/Disables stone and cobble road visuals.
foliageTrampleSounds               true/false 	//Enables/Disables foliage making a noise when it breaks from trampling.
onlyPlayersCreateTrails            true/false 	//Enables/Disables creatures other than players trampling foliage and trampled grass.
flowerTrampling                    true/false	//Enables/Disables flowers breaking when trails are created underneath them.
fernTrampling			   true/false   //Enables/Disables ferns breaking when trails are created underneath them.
onlyTrampleFoliageOnTrailCreation  true/false	//Enables/Disables grass and plants getting trampled before a trail is created.

TRAIL DEVOLUTION (RETURNING TO NATURE)
trampledSoilDevolveDays		   Example: 7.0  //The number of days before an undisturbed trampled soil block will devolve back to native game soil.
trailDevolveDays 	           Example: 60.0 //The number of days before an undisturbed trail block will devolve one trail level.

GRASS BLOCK EVOLUTION (VALUES IN NUMBER OF BLOCK TOUCHES)
normalToSparseGrassTouchCount      Example: 1 	 //The number of touches it takes to evolve a normal grass block to a sparse grass block.
sparseToVerySparseGrassTouchCount  Example: 1    //The number of touches it takes to evolve a sparse grass block to a very sparse grass block.
verySparseToSoilTouchCount	   Example: 1    //The number of touches it takes to evolve a very sparse grass block into a bare soil block.

SOIL TO PRE-TRAIL BLOCK EVOLUTION (VALUES IN NUMBER OF BLOCK TOUCHES)
soilToTrampledSoilTouchCount       Example: 1    //The number of touches it takes to evolve a bare soil block into a trampled soil block (This it the pre-trail level)

PRETRAIL TO TRAIL BLOCK EVOLUTION (This block exists as a mid stage between native game blocks and the trail blocks, consider it a temporary reservation for a possible trail)
trampledSoilToNewTrailTouchCount   Example: 3    //The number of touches it takes to evolve a trampled soil block into a new trail block.

TRAIL BLOCK EVOLUTION
newToEstablishedTrailTouchCount    Example: 25   //The number of touches it takes to evolve a new trail into an established trail.
establishedToDirtRoadTouchCount    Example: 50   //The number of touches it takes to evolve an established trail into a dirt road.
dirtRoadToHighwayTouchCount	   Example: 75   //The number of touches it takes to evolve a dirt road into a highway.
        
OTHER BLOCK EVOLUTIONS
forestFloorToSoilTouchCount	   Example: 2    //The number of touches it takes to evolve bare forest floor into a low fertility soil block. (Forest floor has to progress to soil becoming trail.
cobLoseGrassTouchCount		   Example: 1    //The number of touches it takes for cob to lose one level of grass. (It never evolves into anything, but foot traffic will wear the grass off the block)
peatLoseGrassTouchCount		   Example: 1    //The number of touches it takes for grassy peat to become bare peat. (It never evolves into anything, but foot traffic will wear the grass off the block)
clayLoseGrassTouchCount		   Example: 1    //The number of touches it takes for grassy clay to become bare clay. (It never evolves into anything, but foot traffic will wear the grass off the block)

MINIMUM CREATURE HULL SIZE REQUIRED FOR TRAMPLING
minEntityHullSizeToTrampleX        Example: 0.5  //The minimum size of a creature's X (Horizontal) collision hull size required for a creature to trample grass and foliage.
minEntityHullSizeToTrampleY        Example: 0.75 //The minimum size of a creature's Y (Vertical) collision hull size required for a creature to trample grass and foliage.