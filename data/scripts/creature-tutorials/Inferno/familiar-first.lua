-- Global symbols

print("Starting tutorial for inferno/imp");

local me = "me";
local foe = "foe";

addUnit(FAMILIAR, ATTACKER, 2,2, 20, me);
addUnit(FAMILIAR, DEFENDER, 8,6, 20, foe);
sleep(10);

showMessage(3568);
sleep(20);

local x,y = pos(me);
commandMove(foe, x+3, y+2);
sleep(5);
x,y = pos(foe);
commandMove(me, x-2, y);
sleep(20);

commandMoveAttack(me, foe);
sleep(40);

showMessage(3569);

commandDoSpecial(me, ABILITY_GATING);
sleep(10);
commandDoSpecial(foe, ABILITY_GATING);
sleep(10);

clearMessage();

removeUnit(me);
removeUnit(foe);

print("done");
