import { Slider as SliderPrimitive } from '@base-ui/react/slider';

import { cn } from '@/lib/utils';

type SliderProps = Omit<SliderPrimitive.Root.Props<number>, 'value' | 'onValueChange'> & {
  value: number;
  onValueChange: (value: number) => void;
  gamepadStep?: number;
};

function Slider({
  className,
  value,
  min = 0,
  max = 100,
  step = 1,
  disabled,
  onValueChange,
  gamepadStep = step,
  ...props
}: SliderProps) {
  return (
    <SliderPrimitive.Root
      data-slot="slider"
      className={cn('relative flex w-full touch-none items-center py-2 outline-none', className)}
      value={value}
      min={min}
      max={max}
      step={step}
      disabled={disabled}
      onValueChange={onValueChange}
      {...props}
    >
      <SliderPrimitive.Control className="relative flex h-5 w-full items-center">
        <SliderPrimitive.Track className="relative h-1.5 w-full rounded-full bg-muted">
          <SliderPrimitive.Indicator className="absolute h-full rounded-full bg-primary" />
        </SliderPrimitive.Track>
        <SliderPrimitive.Thumb
          index={0}
          tabIndex={-1}
          className="size-4 rounded-full border border-primary bg-background shadow-sm outline-none focus-visible:ring-2 focus-visible:ring-ring/35"
        />
      </SliderPrimitive.Control>
      <input
        type="range"
        min={min}
        max={max}
        step={step}
        value={value}
        disabled={disabled}
        aria-label={props['aria-label']}
        data-gamepad-slider="true"
        data-gamepad-step={gamepadStep}
        onChange={(event) => onValueChange(Number(event.target.value))}
        className="absolute inset-0 h-full w-full cursor-pointer opacity-0 disabled:cursor-not-allowed"
      />
    </SliderPrimitive.Root>
  );
}

function SliderField({
  label,
  value,
  min,
  max,
  step = 1,
  valueText,
  description,
  onChange,
}: {
  label: string;
  value: number;
  min: number;
  max: number;
  step?: number;
  valueText?: string;
  description?: string;
  onChange: (value: number) => void;
}) {
  return (
    <div>
      <div className="mb-1 flex items-center justify-between gap-3 text-sm">
        <span className="font-medium">{label}</span>
        {valueText && <span className="text-muted-foreground">{valueText}</span>}
      </div>
      <Slider
        min={min}
        max={max}
        step={step}
        value={value}
        onValueChange={onChange}
        aria-label={label}
        gamepadStep={step}
      />
      {description && <div className="mt-1 text-xs text-muted-foreground">{description}</div>}
    </div>
  );
}

export { Slider, SliderField };
