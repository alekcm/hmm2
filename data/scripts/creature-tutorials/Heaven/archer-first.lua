-- Global symbols

print("Starting tutorial for inferno/imp");

local me = "me";
local me2 = "me2";
local me3 = "me3";
local foe = "foe";

addUnit(FAMILIAR, ATTACKER, 2,2, 8, me);
addUnit(IMP, ATTACKER, 2,4, 5, me2);
addUnit(FAMILIAR, ATTACKER, 2,6, 20, me3);
addUnit(ARCHER, DEFENDER, 8,4, 55, foe);

showMessage(3601);
sleep(40);

showMessage(3602);
local x,y = pos(me);
commandMove(me2, x+1, y+1);
sleep(25);


--x,y = pos(foe);
--commandMove(me3, x-1, y);
--sleep(30);

showMessage(3604);

--x,y = pos(foe);
--commandMove(foe, x, y-2);
commandDoSpecial(foe, ABILITY_SCATTER_SHOT, 3, 3);
sleep(20);

showMessage(3603);
sleep(25);
commandMoveAttack(me3, foe);
sleep(20);

commandMoveAttack(foe, me3);
showMessage(3605);
sleep(15);

removeUnit(me);
removeUnit(me2);
removeUnit(me3);
removeUnit(foe);

sleep(25);
clearMessage();

print("done");
