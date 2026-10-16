hero = "Almegir";
m =0;
h = 0;
nCost = 0;
x = 0;
y = 0;
nFloor = 0;

object_name = "SAWMILL";
objects = GetObjectNamesByType( object_name );
for m, h in objects do
	x, y, nFloor = GetObjectPosition(h);
	nCost = CalcHeroMoveCost(hero, x, y, nFloor);
	print("From hero ", hero, " to nearest ", h, "(", object_name, ") at ", x, ", ", y, " is ", nCost);
	monster_tiers = GetGuardsTier(h);
	for mst, tier in monster_tiers do
		print(h , " has guard of creatures of tier: ", tier);
	end;
end;