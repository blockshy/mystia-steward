import type {
  IBeverage,
  ICustomerNormal,
  ICustomerRare,
  IIngredient,
  IRecipe,
  TPlace,
} from '@/lib/types';
import { ALL_PLACES } from '@/lib/types';

const NON_ORDERABLE_RARE_FOOD_TAGS = new Set(['流行喜爱', '流行厌恶']);

export interface RecommendationDataSet {
  recipes: IRecipe[];
  ingredients: IIngredient[];
  beverages: IBeverage[];
  normalCustomers: ICustomerNormal[];
  rareCustomers: ICustomerRare[];
  source: 'runtime' | 'unavailable';
  status: string;
}

export const DEFAULT_RECOMMENDATION_DATA: RecommendationDataSet = {
  recipes: [],
  ingredients: [],
  beverages: [],
  normalCustomers: [],
  rareCustomers: [],
  source: 'unavailable',
  status: '等待游戏运行时数据',
};

export interface RuntimeDataCatalogSnapshot {
  isComplete: boolean;
  source: string;
  status: string;
  recipes: Array<Partial<IRecipe> & { id: number; recipeId: number; name: string }>;
  ingredients: Array<Partial<IIngredient> & { id: number; name: string }>;
  beverages: Array<Partial<IBeverage> & { id: number; name: string }>;
  normalCustomers: Array<Partial<ICustomerNormal> & { id: number; name: string }>;
  rareCustomers: Array<Partial<ICustomerRare> & { id: number; name: string }>;
  foodTagIdMap?: Record<string, string>;
}

export function buildRecommendationDataSet(
  runtimeData: RuntimeDataCatalogSnapshot | null | undefined,
): RecommendationDataSet {
  if (!runtimeData?.isComplete) return DEFAULT_RECOMMENDATION_DATA;

  const recipes = runtimeData.recipes
    .map(normalizeRuntimeRecipe)
    .filter((item): item is IRecipe => item !== null);
  const ingredients = runtimeData.ingredients
    .map(normalizeRuntimeIngredient)
    .filter((item): item is IIngredient => item !== null);
  const beverages = runtimeData.beverages
    .map(normalizeRuntimeBeverage)
    .filter((item): item is IBeverage => item !== null);
  const normalCustomers = runtimeData.normalCustomers
    .map(normalizeRuntimeNormalCustomer)
    .filter((item): item is ICustomerNormal => item !== null);
  const rareCustomers = runtimeData.rareCustomers
    .map(normalizeRuntimeRareCustomerData)
    .filter((item): item is ICustomerRare => item !== null);

  if (
    recipes.length === 0
    || ingredients.length === 0
    || beverages.length === 0
    || normalCustomers.length === 0
    || rareCustomers.length === 0
  ) {
    return {
      ...DEFAULT_RECOMMENDATION_DATA,
      status: runtimeData.status || runtimeData.source || '运行时数据不完整',
    };
  }

  return {
    recipes,
    ingredients,
    beverages,
    normalCustomers,
    rareCustomers,
    source: 'runtime',
    status: runtimeData.status || runtimeData.source || 'game runtime',
  };
}

export function buildRecommendationDataIndexes(data: RecommendationDataSet) {
  return {
    ingredientByName: new Map(data.ingredients.map((ingredient) => [ingredient.name, ingredient])),
    ingredientIdByName: new Map(data.ingredients.map((ingredient) => [ingredient.name, ingredient.id])),
    ingredientNameById: new Map(data.ingredients.map((ingredient) => [ingredient.id, ingredient.name])),
    beverageNameById: new Map(data.beverages.map((beverage) => [beverage.id, beverage.name])),
    recipeByFoodId: new Map(data.recipes.map((recipe) => [recipe.id, recipe])),
  };
}

function normalizeRuntimeRecipe(value: RuntimeDataCatalogSnapshot['recipes'][number]): IRecipe | null {
  if (!Number.isFinite(value.id) || !Number.isFinite(value.recipeId) || !value.name) return null;
  return {
    id: value.id,
    recipeId: value.recipeId,
    name: value.name,
    description: value.description ?? '',
    ingredients: normalizeStringArray(value.ingredients),
    positiveTags: normalizeStringArray(value.positiveTags),
    negativeTags: normalizeStringArray(value.negativeTags),
    cooker: value.cooker ?? '',
    baseCookTime: value.baseCookTime ?? 0,
    dlc: value.dlc ?? 0,
    level: value.level ?? 0,
    price: value.price ?? 0,
    from: value.from ?? {},
  };
}

function normalizeRuntimeIngredient(value: RuntimeDataCatalogSnapshot['ingredients'][number]): IIngredient | null {
  if (!Number.isFinite(value.id) || !value.name) return null;
  return {
    id: value.id,
    name: value.name,
    description: value.description ?? '',
    type: value.type ?? '',
    tags: normalizeStringArray(value.tags),
    dlc: value.dlc ?? 0,
    level: value.level ?? 0,
    price: value.price ?? 0,
    from: value.from ?? {},
  };
}

function normalizeRuntimeBeverage(value: RuntimeDataCatalogSnapshot['beverages'][number]): IBeverage | null {
  if (!Number.isFinite(value.id) || !value.name) return null;
  return {
    id: value.id,
    name: value.name,
    description: value.description ?? '',
    tags: normalizeStringArray(value.tags),
    dlc: value.dlc ?? 0,
    level: value.level ?? 0,
    price: value.price ?? 0,
    from: value.from ?? {},
  };
}

function normalizeRuntimeNormalCustomer(
  value: RuntimeDataCatalogSnapshot['normalCustomers'][number],
): ICustomerNormal | null {
  if (!Number.isFinite(value.id) || !isUsableRuntimeName(value.name)) return null;
  const places = normalizePlaces(value.places);
  const positiveTags = normalizeStringArray(value.positiveTags);
  const beverageTags = normalizeStringArray(value.beverageTags);
  if (places.length === 0 || (positiveTags.length === 0 && beverageTags.length === 0)) return null;

  return {
    id: value.id,
    name: value.name,
    description: value.description ?? '',
    dlc: value.dlc ?? 0,
    places,
    positiveTags,
    beverageTags,
  };
}

function normalizeRuntimeRareCustomerData(
  value: RuntimeDataCatalogSnapshot['rareCustomers'][number],
): ICustomerRare | null {
  if (!Number.isFinite(value.id) || !isUsableRuntimeName(value.name)) return null;
  const places = normalizePlaces(value.places);
  const positiveTags = normalizeStringArray(value.positiveTags).filter(isOrderableRareFoodTag);
  const beverageTags = normalizeStringArray(value.beverageTags);
  if (places.length === 0 || positiveTags.length === 0 || beverageTags.length === 0) return null;

  return {
    id: value.id,
    name: value.name,
    description: value.description ?? '',
    dlc: value.dlc ?? 0,
    places,
    price: value.price ?? [0, 0],
    enduranceLimit: value.enduranceLimit ?? 1,
    positiveTags,
    negativeTags: normalizeStringArray(value.negativeTags),
    beverageTags,
    positiveTagMapping: value.positiveTagMapping ?? {},
    beverageTagMapping: value.beverageTagMapping ?? {},
    collection: value.collection ?? false,
    evaluation: value.evaluation ?? {},
    spellCards: value.spellCards ?? { positive: [], negative: [] },
  };
}

function normalizeStringArray(value: unknown): string[] {
  return Array.isArray(value)
    ? [...new Set(value.map((item) => String(item).trim()).filter(Boolean))]
    : [];
}

function normalizePlaces(value: unknown): TPlace[] {
  const places = normalizeStringArray(value)
    .filter((place): place is TPlace => (ALL_PLACES as string[]).includes(place));
  return places;
}

function isUsableRuntimeName(value: unknown): value is string {
  const name = String(value ?? '').trim();
  return Boolean(name)
    && name !== 'missing'
    && name !== 'null'
    && !name.includes('?')
    && !name.startsWith('#');
}

function isOrderableRareFoodTag(tag: string): boolean {
  return Boolean(tag) && !NON_ORDERABLE_RARE_FOOD_TAGS.has(tag);
}
