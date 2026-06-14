import { Switch as MantineSwitch } from '@mantine/core';
import type { SwitchProps as MantineSwitchProps } from '@mantine/core';
import type { LabelHTMLAttributes } from 'react';

import { cn } from '@/lib/utils';

type SwitchProps = Omit<MantineSwitchProps, 'checked' | 'onChange'> & {
  checked?: boolean;
  onCheckedChange?: (checked: boolean) => void;
};

function Switch({ className, checked, onCheckedChange, ...props }: SwitchProps) {
  return (
    <MantineSwitch
      data-slot="switch"
      className={cn('steward-switch', className)}
      checked={checked}
      color="steward"
      size="sm"
      onChange={(event) => onCheckedChange?.(event.currentTarget.checked)}
      {...props}
    />
  );
}

type SwitchFieldProps = Omit<LabelHTMLAttributes<HTMLLabelElement>, 'onChange'> & {
  label: string;
  checked: boolean;
  onCheckedChange: (checked: boolean) => void;
  disabled?: boolean;
};

function SwitchField({
  label,
  checked,
  onCheckedChange,
  className,
  disabled,
  title,
  ...props
}: SwitchFieldProps) {
  return (
    <label
      className={cn('steward-switch-field flex items-center gap-2.5 text-sm', disabled && 'text-muted-foreground', className)}
      title={title}
      {...props}
    >
      <Switch checked={checked} onCheckedChange={onCheckedChange} disabled={disabled} />
      <span className="whitespace-nowrap">{label}</span>
    </label>
  );
}

export { Switch, SwitchField };
export type { SwitchFieldProps, SwitchProps };
