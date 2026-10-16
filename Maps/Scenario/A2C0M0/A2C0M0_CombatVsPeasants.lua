print("Combat script started...");

VOICEOVER_EXPLAIN_BLOODRAGE="/Maps/Scenario/A2C0M0/C0M1_V02_Quroq_01sound.xdb#xpointer(/Sound)";
VOICEOVER_BLOODRAGE_GROW="/Maps/Scenario/A2C0M0/C0M1_V03_Quroq_01sound.xdb#xpointer(/Sound)";

ATTACKER_CREATURE = 0;
ATTACKER_HERO = 1;
DEFENDER_CREATURE = 2;

isRageBonusMessageShowed = 0;
isDefendsMessageShowed = 0;
isAttacksMessageShowed = 0;
currentCreatureType = 1;

while combatStarted() == nil do
	sleep(1)
end;
print("combat is started...");

Play2DSound( VOICEOVER_EXPLAIN_BLOODRAGE );

quroqsCreatures = GetAttackerCreatures();
quroqsCreatures.n = length( quroqsCreatures );

function WaitWhileTutorialMsgBoxOpen()
	--combatSetPause( 1 );
	while IsTutorialMessageBoxOpen() ~= nil do sleep(1); end;
	--combatSetPause( 0 );
end;

function ShowRageLevelTutorialMessage( creatureName )
	print("ShowRageLevelTutorialMessage: function for creature ", creatureName, " started" );
	repeat sleep(1); until GetRageLevel( creatureName ) == 1 and currentCreatureType == DEFENDER_CREATURE;
	print("ShowRageLevelTutorialMessage: creature ", creatureName, " has gained rage bonus");
	if isRageBonusMessageShowed == 0 then
		isRageBonusMessageShowed = 1;
		TutorialMessageBox( "Is_FirstRageBonusGained" );
		print("TUTORIAL:  Creature has gained rage bonus for attack!");
	else
		print("Rage bonus already gained");
	end;
	print("ShowRageLevelTutorialMessage: end of function");
end;

function ShowMessageIfCreatureDefends( creatureName )
	print("ShowMessageIfCreatureDefends: function for creature ",creatureName," started");
	repeat 
		previousRagePoints = GetRagePoints( creatureName );
		sleep(3);
	until previousRagePoints > GetRagePoints( creatureName );
	if isDefendsMessageShowed == 0 then
		isDefendsMessageShowed = 1;
		TutorialMessageBox( "Is_CreatureDefends" );
		WaitWhileTutorialMsgBoxOpen();
		print("TUTORIAL:  Creature has lost rage points for defense!");
	else
		print("ShowMessageIfCreatureDefends: bouns message already showed");
	end;
	print("ShowMessageIfCreatureDefends: function for creature ",creatureName," finished");
end;

function ShowMessageIfCreatureAttacks( creatureName )
	print("ShowMessageIfCreatureAttacks: function for creature ",creatureName," started");
	repeat 
		previousRagePoints = GetRagePoints( creatureName );
		sleep(1);
	until previousRagePoints < GetRagePoints( creatureName ) and currentCreatureType == ATTACKER_CREATURE;
	if isAttacksMessageShowed == 0 then
		isAttacksMessageShowed = 1;
		TutorialMessageBox( "Is_CreatureAttacks" );
		Play2DSound( VOICEOVER_BLOODRAGE_GROW );
		WaitWhileTutorialMsgBoxOpen();
		print("TUTORIAL:  Creature has gained rage points for attack!");
	else
		print("ShowMessageIfCreatureDefends: bonus message already showed");
	end;
	print("ShowMessageIfCreatureAttacks: function for creature ",creatureName," finished");
end;

function AttackerCreatureMove( creatureName )
	currentCreatureType = ATTACKER_CREATURE;
	print("currentCreatureType =",currentCreatureType, ". Attacker creature turn");
	return nil;
end;
   
function AttackerHeroMove( heroName )
	currentCreatureType = ATTACKER_HERO;
	print("currentCreatureType =",currentCreatureType, ". Attacker hero turn");
	return nil;
end;

function DefenderCreatureMove( creatureName )
	currentCreatureType = DEFENDER_CREATURE;
	print("currentCreatureType =",currentCreatureType, ". Defender creature turn");
	return nil;
end;

for i=0, quroqsCreatures.n-1 do
	startThread( ShowRageLevelTutorialMessage, quroqsCreatures[i] );
	startThread( ShowMessageIfCreatureDefends, quroqsCreatures[i] );
	startThread( ShowMessageIfCreatureAttacks, quroqsCreatures[i] );
end;
print("MAIN: All functions started");