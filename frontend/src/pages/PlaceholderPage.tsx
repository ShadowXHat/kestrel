import React from 'react';
import { Card, CardHeader, CardTitle, CardDescription, CardContent } from '../components/ui/Card';
import { Clock } from 'lucide-react';

export interface PlaceholderPageProps {
  title: string;
  phase: string;
  description: string;
  icon?: React.ReactNode;
}

export const PlaceholderPage: React.FC<PlaceholderPageProps> = ({
  title,
  phase,
  description,
  icon,
}) => {
  return (
    <div className="p-6 md:p-10 max-w-4xl mx-auto space-y-6">
      <Card variant="glass" className="border-kestrel-border rounded-xl">
        <CardHeader className="pb-4">
          <div className="flex items-center gap-3">
            {icon ? (
              <div className="p-2.5 rounded-lg bg-sky-950/80 border border-sky-500/30 text-sky-400">
                {icon}
              </div>
            ) : (
              <div className="p-2.5 rounded-lg bg-kestrel-surface border border-kestrel-border text-kestrel-muted">
                <Clock className="w-5 h-5" />
              </div>
            )}
            <div>
              <CardTitle className="text-lg text-white font-sans">{title}</CardTitle>
              <CardDescription className="text-xs text-kestrel-muted font-mono mt-0.5">
                Target Roadmap: {phase}
              </CardDescription>
            </div>
          </div>
        </CardHeader>
        <CardContent className="space-y-4">
          <div className="p-4 bg-[#0b1019] border border-kestrel-border rounded-xl space-y-2">
            <div className="flex items-center gap-2 text-xs font-mono text-amber-400">
              <span className="w-2 h-2 rounded-full bg-amber-400 animate-pulse" />
              <span className="font-semibold">Available in a later phase</span>
            </div>
            <p className="text-xs text-kestrel-muted leading-relaxed font-sans">
              {description}
            </p>
          </div>
        </CardContent>
      </Card>
    </div>
  );
};
