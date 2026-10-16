-- Global symbols

print("Starting tutorial for inferno/imp");

local me = "me";
local foe = "foe";

addUnit(IMP, ATTACKER, 2,5, 22, me);
addUnit(FAMILIAR, DEFENDER, 8,2, 21, foe);
showMessage(3600);
sleep(20);


local x,y = pos(me);
commandMove(me, x+2, y-1);
sleep(10);

commandMoveAttack(foe, me);
sleep(10);

commandMove(me, 4, 3);
sleep(5);
commandMoveAttack(me, foe);
sleep(25);


commandDoSpecial(me, ABILITY_GATING);
sleep(10);
--commandDoSpecial(foe, ABILITY_GATING);
--sleep(40);

clearMessage();

removeUnit(me);
removeUnit(foe);

print("done");
