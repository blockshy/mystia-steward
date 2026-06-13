import { cn } from '@/lib/utils';

type ChoiceOption<TValue extends string> = {
  value: TValue;
  label: string;
  description: string;
};

function ChoiceGroup<TValue extends string>({
  label,
  value,
  options,
  onChange,
}: {
  label: string;
  value: TValue;
  options: ChoiceOption<TValue>[];
  onChange: (value: TValue) => void;
}) {
  return (
    <div>
      <div className="mb-2 text-sm font-medium">{label}</div>
      <div className={cn('grid gap-2', options.length > 2 ? 'grid-cols-3' : 'grid-cols-2')}>
        {options.map((option) => (
          <button
            key={option.value}
            type="button"
            onClick={() => onChange(option.value)}
            className={cn(
              'rounded-md border p-2 text-left text-sm transition-colors outline-none focus-visible:border-ring focus-visible:ring-2 focus-visible:ring-ring/35',
              value === option.value
                ? 'border-primary bg-primary/10 text-foreground'
                : 'border-border bg-background/55 text-foreground hover:bg-muted',
            )}
          >
            <div className="font-medium">{option.label}</div>
            <div className="mt-1 text-xs text-muted-foreground">{option.description}</div>
          </button>
        ))}
      </div>
    </div>
  );
}

export { ChoiceGroup };
export type { ChoiceOption };
