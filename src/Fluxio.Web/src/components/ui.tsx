import type { ButtonHTMLAttributes, ReactNode } from 'react';
import { Icon, type IconName } from './Icon';

export const cx = (...names: (string | false | null | undefined)[]) => names.filter(Boolean).join(' ');

export function Logo({ compact = false }: { compact?: boolean }) {
  return (
    <span className="logo">
      <svg className="logo__mark" viewBox="0 0 48 48" width="32" height="32" aria-hidden="true" focusable="false">
        <defs>
          <linearGradient id="fluxio-logo-gradient" x1="0" y1="0" x2="1" y2="1">
            <stop offset="0" stopColor="#22d3ee" />
            <stop offset="1" stopColor="#3b82f6" />
          </linearGradient>
        </defs>
        <rect x="2" y="2" width="44" height="44" rx="13" fill="url(#fluxio-logo-gradient)" />
        <path d="M14 19h18l-5-5M34 29H16l5 5" fill="none" stroke="#0b1020" strokeWidth="3.6" strokeLinecap="round" strokeLinejoin="round" />
      </svg>
      {!compact && <span className="logo__word">Fluxio</span>}
    </span>
  );
}

type Variant = 'primary' | 'secondary' | 'ghost' | 'danger';

interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: Variant;
  block?: boolean;
  icon?: IconName;
  loading?: boolean;
  size?: 'md' | 'lg';
}

export function Button({ variant = 'secondary', block, icon, loading, size = 'md', className, children, disabled, type = 'button', ...rest }: ButtonProps) {
  return (
    <button
      type={type}
      className={cx('btn', `btn--${variant}`, size === 'lg' && 'btn--lg', block && 'btn--block', className)}
      disabled={disabled || loading}
      aria-busy={loading || undefined}
      {...rest}
    >
      {loading ? <Spinner small /> : icon ? <Icon name={icon} size={20} /> : null}
      <span>{children}</span>
    </button>
  );
}

export function IconButton({ icon, label, onClick, className }: { icon: IconName; label: string; onClick: () => void; className?: string }) {
  return (
    <button type="button" className={cx('icon-btn', className)} onClick={onClick} aria-label={label} title={label}>
      <Icon name={icon} size={20} />
    </button>
  );
}

export function Spinner({ small = false, label }: { small?: boolean; label?: string }) {
  return (
    <span className={cx('spinner', small && 'spinner--small')} role={label ? 'status' : undefined} aria-label={label}>
      <span className="spinner__ring" aria-hidden="true" />
    </span>
  );
}

export function ProgressBar({ value, label, tone = 'accent' }: { value: number; label: string; tone?: 'accent' | 'success' | 'danger' | 'muted' }) {
  const clamped = Math.max(0, Math.min(100, Math.round(value)));
  return (
    <div className={cx('progress', `progress--${tone}`)} role="progressbar" aria-valuemin={0} aria-valuemax={100} aria-valuenow={clamped} aria-label={label}>
      <div className="progress__bar" style={{ width: `${clamped}%` }} />
    </div>
  );
}

export type Tone = 'ok' | 'warn' | 'error' | 'info' | 'busy';

export function Pill({ tone, children }: { tone: Tone; children: ReactNode }) {
  return (
    <span className={cx('pill', `pill--${tone}`)}>
      <span className="pill__dot" aria-hidden="true" />
      {children}
    </span>
  );
}

export function Notice({ tone = 'info', icon, title, children, action }: { tone?: 'info' | 'warning' | 'error' | 'success'; icon?: IconName; title?: string; children?: ReactNode; action?: ReactNode }) {
  const defaultIcon: IconName = tone === 'error' ? 'alert' : tone === 'warning' ? 'alert' : tone === 'success' ? 'check' : 'info';
  return (
    <div className={cx('notice', `notice--${tone}`)} role={tone === 'error' ? 'alert' : 'status'}>
      <Icon name={icon ?? defaultIcon} size={20} className="notice__icon" />
      <div className="notice__body">
        {title && <strong className="notice__title">{title}</strong>}
        {children && <div className="notice__text">{children}</div>}
        {action && <div className="notice__action">{action}</div>}
      </div>
    </div>
  );
}

export function StatePanel({ icon, tone = 'accent', title, children, actions, busy }: { icon: IconName; tone?: 'accent' | 'danger' | 'warning' | 'success' | 'receive'; title: string; children?: ReactNode; actions?: ReactNode; busy?: boolean }) {
  return (
    <section className="state-panel" aria-live="polite">
      <div className={cx('state-panel__icon', `state-panel__icon--${tone}`)}>{busy ? <Spinner /> : <Icon name={icon} size={30} />}</div>
      <h1 className="state-panel__title">{title}</h1>
      {children && <div className="state-panel__body">{children}</div>}
      {actions && <div className="state-panel__actions">{actions}</div>}
    </section>
  );
}

export function PageHeader({ title, subtitle, onBack, backLabel }: { title: string; subtitle?: string; onBack?: () => void; backLabel?: string }) {
  return (
    <div className="page-header">
      {onBack && (
        <button type="button" className="back-btn" onClick={onBack}>
          <Icon name="chevronLeft" size={20} />
          <span>{backLabel}</span>
        </button>
      )}
      <h1>{title}</h1>
      {subtitle && <p className="muted page-header__subtitle">{subtitle}</p>}
    </div>
  );
}
